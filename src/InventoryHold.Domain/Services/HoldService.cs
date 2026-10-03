using InventoryHold.Domain.Abstractions;
using InventoryHold.Domain.Commands;
using InventoryHold.Domain.Entities;
using InventoryHold.Domain.Repositories;

namespace InventoryHold.Domain.Services;

public sealed class HoldService
{
    private readonly IInventoryHoldStore _store;
    private readonly IInventoryCache _cache;
    private readonly IHoldEventPublisher _publisher;
    private readonly IClock _clock;
    private readonly IHoldDurationProvider _duration;

    public HoldService(
        IInventoryHoldStore store,
        IInventoryCache cache,
        IHoldEventPublisher publisher,
        IClock clock,
        IHoldDurationProvider duration)
    {
        _store = store;
        _cache = cache;
        _publisher = publisher;
        _clock = clock;
        _duration = duration;
    }

    public async Task<ServiceResult<Hold>> CreateAsync(CreateHoldCommand command, CancellationToken cancellationToken)
    {
        var validationError = Validate(command);
        if (validationError is not null)
        {
            return validationError;
        }

        var lines = command.Items!
            .Select(item => new CreateHoldLine(item.ProductId.Trim(), item.Quantity))
            .ToList();

        var products = await _store.GetProductsByIdsAsync(
            lines.Select(line => line.ProductId).ToArray(),
            cancellationToken);
        var productsById = products.ToDictionary(product => product.ProductId, StringComparer.Ordinal);
        var missingId = lines.Select(line => line.ProductId).FirstOrDefault(id => !productsById.ContainsKey(id));
        if (missingId is not null)
        {
            return ServiceResult<Hold>.Fail(
                ErrorCodes.ProductNotFound,
                $"Product '{missingId}' was not found.",
                Status404);
        }

        var now = _clock.UtcNow;
        var hold = new Hold(
            Guid.NewGuid().ToString("D"),
            HoldStatus.Active,
            now,
            now.Add(_duration.Value),
            lines.Select(line => new HoldItem(line.ProductId, productsById[line.ProductId].Name, line.Quantity)).ToList(),
            command.ClientRequestId);

        var placed = await _store.PlaceHoldAsync(hold, cancellationToken);
        switch (placed.Status)
        {
            case PlaceHoldStatus.Placed:
                await _cache.InvalidateInventoryAsync(cancellationToken);
                await _publisher.PublishCreatedAsync(placed.Hold!, cancellationToken);
                return ServiceResult<Hold>.Ok(placed.Hold!);
            case PlaceHoldStatus.AlreadyExists:
                return ServiceResult<Hold>.Ok(placed.Hold!);
            case PlaceHoldStatus.InsufficientStock:
                return ServiceResult<Hold>.Fail(
                    ErrorCodes.InsufficientStock,
                    "Not enough stock to place this hold.",
                    Status409);
            case PlaceHoldStatus.ProductNotFound:
                return ServiceResult<Hold>.Fail(
                    ErrorCodes.ProductNotFound,
                    "One or more products were not found.",
                    Status404);
            default:
                return ServiceResult<Hold>.Fail(ErrorCodes.Validation, "The hold could not be placed.", Status400);
        }
    }

    public async Task<ServiceResult<Hold>> GetAsync(string holdId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(holdId))
        {
            return ServiceResult<Hold>.Fail(ErrorCodes.Validation, "Hold id is required.", Status400);
        }

        var hold = await _store.GetHoldAsync(holdId, cancellationToken);
        if (hold is null)
        {
            return ServiceResult<Hold>.Fail(ErrorCodes.HoldNotFound, "Hold was not found.", Status404);
        }

        if (!hold.IsActiveAndDue(_clock.UtcNow))
        {
            return ServiceResult<Hold>.Ok(hold);
        }

        return await CompleteExpiryAsync(holdId, cancellationToken, returnConflict: false);
    }

    public async Task<IReadOnlyList<Hold>> ListActiveAsync(CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var holds = await _store.ListActiveUnexpiredAsync(now, cancellationToken);
        return holds
            .Where(hold => hold.Status == HoldStatus.Active && hold.ExpiresAtUtc > now)
            .ToList();
    }

    public async Task<ServiceResult<Hold>> ReleaseAsync(string holdId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(holdId))
        {
            return ServiceResult<Hold>.Fail(ErrorCodes.Validation, "Hold id is required.", Status400);
        }

        var hold = await _store.GetHoldAsync(holdId, cancellationToken);
        if (hold is null)
        {
            return ServiceResult<Hold>.Fail(ErrorCodes.HoldNotFound, "Hold was not found.", Status404);
        }

        if (hold.Status == HoldStatus.Released)
        {
            return ServiceResult<Hold>.Fail(
                ErrorCodes.HoldAlreadyReleased,
                "Hold is already released.",
                Status409);
        }

        if (hold.Status == HoldStatus.Expired)
        {
            return ServiceResult<Hold>.Fail(ErrorCodes.HoldExpired, "Hold is already expired.", Status409);
        }

        if (hold.IsActiveAndDue(_clock.UtcNow))
        {
            return await CompleteExpiryAsync(holdId, cancellationToken, returnConflict: true);
        }

        var transition = await _store.TryTransitionAndRestoreAsync(holdId, HoldStatus.Released, cancellationToken);
        if (transition.Status == TransitionStatus.Applied && transition.Hold is not null)
        {
            await _cache.InvalidateInventoryAsync(cancellationToken);
            await _publisher.PublishReleasedAsync(transition.Hold, cancellationToken);
            return ServiceResult<Hold>.Ok(transition.Hold);
        }

        return MapLostTransition(transition);
    }

    public async Task ExpireDueHoldsAsync(CancellationToken cancellationToken)
    {
        var due = await _store.ListDueForExpiryAsync(_clock.UtcNow, cancellationToken);
        foreach (var hold in due)
        {
            var transition = await _store.TryTransitionAndRestoreAsync(hold.HoldId, HoldStatus.Expired, cancellationToken);
            if (transition.Status != TransitionStatus.Applied || transition.Hold is null)
            {
                continue;
            }

            await _cache.InvalidateInventoryAsync(cancellationToken);
            await _publisher.PublishExpiredAsync(transition.Hold, cancellationToken);
        }
    }

    private async Task<ServiceResult<Hold>> CompleteExpiryAsync(
        string holdId,
        CancellationToken cancellationToken,
        bool returnConflict)
    {
        var transition = await _store.TryTransitionAndRestoreAsync(holdId, HoldStatus.Expired, cancellationToken);
        if (transition.Status == TransitionStatus.Applied && transition.Hold is not null)
        {
            await _cache.InvalidateInventoryAsync(cancellationToken);
            await _publisher.PublishExpiredAsync(transition.Hold, cancellationToken);
            if (returnConflict)
            {
                return ServiceResult<Hold>.Fail(
                    ErrorCodes.HoldExpired,
                    "Hold had already expired. Inventory was restored.",
                    Status409);
            }

            return ServiceResult<Hold>.Ok(transition.Hold);
        }

        if (transition.Hold is null)
        {
            return ServiceResult<Hold>.Fail(ErrorCodes.HoldNotFound, "Hold was not found.", Status404);
        }

        if (returnConflict)
        {
            return MapLostTransition(transition);
        }

        return ServiceResult<Hold>.Ok(transition.Hold);
    }

    private static ServiceResult<Hold> MapLostTransition(TransitionResult transition)
    {
        if (transition.Hold is null || transition.Status == TransitionStatus.NotFound)
        {
            return ServiceResult<Hold>.Fail(ErrorCodes.HoldNotFound, "Hold was not found.", Status404);
        }

        return transition.Hold.Status switch
        {
            HoldStatus.Released => ServiceResult<Hold>.Fail(
                ErrorCodes.HoldAlreadyReleased,
                "Hold is already released.",
                Status409),
            HoldStatus.Expired => ServiceResult<Hold>.Fail(
                ErrorCodes.HoldExpired,
                "Hold is already expired.",
                Status409),
            _ => ServiceResult<Hold>.Fail(ErrorCodes.HoldNotActive, "Hold is not active.", Status409)
        };
    }

    private static ServiceResult<Hold>? Validate(CreateHoldCommand command)
    {
        if (command.Items is null || command.Items.Count == 0)
        {
            return ServiceResult<Hold>.Fail(ErrorCodes.Validation, "At least one item is required.", Status400);
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in command.Items)
        {
            if (item is null || string.IsNullOrWhiteSpace(item.ProductId))
            {
                return ServiceResult<Hold>.Fail(ErrorCodes.Validation, "Every item needs a product id.", Status400);
            }

            if (item.Quantity <= 0)
            {
                return ServiceResult<Hold>.Fail(
                    ErrorCodes.Validation,
                    "Quantity must be a positive integer.",
                    Status400);
            }

            if (!seen.Add(item.ProductId.Trim()))
            {
                return ServiceResult<Hold>.Fail(
                    ErrorCodes.DuplicateProduct,
                    $"Product '{item.ProductId.Trim()}' is listed more than once.",
                    Status400);
            }
        }

        return null;
    }

    private const int Status400 = 400;
    private const int Status404 = 404;
    private const int Status409 = 409;
}
