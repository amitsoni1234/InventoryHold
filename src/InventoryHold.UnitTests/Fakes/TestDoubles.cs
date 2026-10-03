using InventoryHold.Domain;
using InventoryHold.Domain.Abstractions;
using InventoryHold.Domain.Entities;
using InventoryHold.Domain.Repositories;

namespace InventoryHold.UnitTests.Fakes;

internal sealed class FixedClock : IClock
{
    public DateTime UtcNow { get; set; } = new(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc);
}

internal sealed class FixedDuration : IHoldDurationProvider
{
    public TimeSpan Value { get; init; } = TimeSpan.FromMinutes(15);
}

internal sealed class FakeCache : IInventoryCache
{
    public IReadOnlyList<Product>? Cached { get; set; }

    public int Invalidations { get; private set; }

    public int Reads { get; private set; }

    public int Writes { get; private set; }

    public bool ThrowOnRead { get; set; }

    public Task<IReadOnlyList<Product>?> GetInventoryAsync(CancellationToken cancellationToken)
    {
        Reads++;
        if (ThrowOnRead)
        {
            throw new InvalidOperationException("Inventory cache must not be read while placing a hold.");
        }

        return Task.FromResult(Cached);
    }

    public Task SetInventoryAsync(IReadOnlyList<Product> products, CancellationToken cancellationToken)
    {
        Writes++;
        Cached = products.ToList();
        return Task.CompletedTask;
    }

    public Task InvalidateInventoryAsync(CancellationToken cancellationToken)
    {
        Invalidations++;
        Cached = null;
        return Task.CompletedTask;
    }
}

internal sealed class FakePublisher : IHoldEventPublisher
{
    public List<Hold> Created { get; } = [];

    public List<Hold> Released { get; } = [];

    public List<Hold> Expired { get; } = [];

    public Task PublishCreatedAsync(Hold hold, CancellationToken cancellationToken)
    {
        Created.Add(hold);
        return Task.CompletedTask;
    }

    public Task PublishReleasedAsync(Hold hold, CancellationToken cancellationToken)
    {
        Released.Add(hold);
        return Task.CompletedTask;
    }

    public Task PublishExpiredAsync(Hold hold, CancellationToken cancellationToken)
    {
        Expired.Add(hold);
        return Task.CompletedTask;
    }
}

internal sealed class FakeStore : IInventoryHoldStore
{
    private readonly object _gate = new();

    public Dictionary<string, Product> Products { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, Hold> Holds { get; } = new(StringComparer.Ordinal);

    public bool LoseNextTransition { get; set; }

    public IReadOnlyList<Hold>? ActiveOverride { get; set; }

    public void AddProduct(string id, string name, int quantity) =>
        Products[id] = new Product(id, name, quantity);

    public Task<IReadOnlyList<Product>> GetAllProductsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Product>>(Products.Values.OrderBy(product => product.Name).ToList());

    public Task<IReadOnlyList<Product>> GetProductsByIdsAsync(
        IReadOnlyCollection<string> productIds,
        CancellationToken cancellationToken)
    {
        var found = productIds
            .Where(Products.ContainsKey)
            .Select(id => Products[id])
            .ToList();
        return Task.FromResult<IReadOnlyList<Product>>(found);
    }

    public Task SeedProductsIfEmptyAsync(IReadOnlyList<Product> products, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task<PlaceHoldResult> PlaceHoldAsync(Hold hold, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (hold.ClientRequestId is not null)
            {
                var existing = Holds.Values.FirstOrDefault(saved => saved.ClientRequestId == hold.ClientRequestId);
                if (existing is not null)
                {
                    return Task.FromResult(new PlaceHoldResult(PlaceHoldStatus.AlreadyExists, existing));
                }
            }

            foreach (var item in hold.Items)
            {
                if (!Products.ContainsKey(item.ProductId))
                {
                    return Task.FromResult(new PlaceHoldResult(PlaceHoldStatus.ProductNotFound, null));
                }

                if (Products[item.ProductId].AvailableQuantity < item.Quantity)
                {
                    return Task.FromResult(new PlaceHoldResult(PlaceHoldStatus.InsufficientStock, null));
                }
            }

            foreach (var item in hold.Items)
            {
                var product = Products[item.ProductId];
                Products[item.ProductId] = new Product(product.ProductId, product.Name, product.AvailableQuantity - item.Quantity);
            }

            Holds[hold.HoldId] = hold;
            return Task.FromResult(new PlaceHoldResult(PlaceHoldStatus.Placed, hold));
        }
    }

    public Task<Hold?> GetHoldAsync(string holdId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(Holds.TryGetValue(holdId, out var hold) ? hold : null);
        }
    }

    public Task<IReadOnlyList<Hold>> ListActiveUnexpiredAsync(DateTime utcNow, CancellationToken cancellationToken)
    {
        if (ActiveOverride is not null)
        {
            return Task.FromResult(ActiveOverride);
        }

        lock (_gate)
        {
            IReadOnlyList<Hold> holds = Holds.Values
                .Where(hold => hold.Status == HoldStatus.Active && hold.ExpiresAtUtc > utcNow)
                .ToList();
            return Task.FromResult(holds);
        }
    }

    public Task<IReadOnlyList<Hold>> ListDueForExpiryAsync(DateTime utcNow, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            IReadOnlyList<Hold> holds = Holds.Values
                .Where(hold => hold.Status == HoldStatus.Active && hold.ExpiresAtUtc <= utcNow)
                .ToList();
            return Task.FromResult(holds);
        }
    }

    public Task<TransitionResult> TryTransitionAndRestoreAsync(
        string holdId,
        HoldStatus targetStatus,
        CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (!Holds.TryGetValue(holdId, out var hold))
            {
                return Task.FromResult(new TransitionResult(TransitionStatus.NotFound, null));
            }

            if (LoseNextTransition)
            {
                LoseNextTransition = false;
                var current = hold.WithStatus(HoldStatus.Expired);
                Holds[holdId] = current;
                return Task.FromResult(new TransitionResult(TransitionStatus.NotActive, current));
            }

            if (hold.Status != HoldStatus.Active)
            {
                return Task.FromResult(new TransitionResult(TransitionStatus.NotActive, hold));
            }

            var updated = hold.WithStatus(targetStatus);
            Holds[holdId] = updated;
            foreach (var item in updated.Items)
            {
                var product = Products[item.ProductId];
                Products[item.ProductId] = new Product(product.ProductId, product.Name, product.AvailableQuantity + item.Quantity);
            }

            return Task.FromResult(new TransitionResult(TransitionStatus.Applied, updated));
        }
    }
}
