using InventoryHold.Domain.Entities;

namespace InventoryHold.Domain.Repositories;

public enum PlaceHoldStatus
{
    Placed,
    AlreadyExists,
    InsufficientStock,
    ProductNotFound
}

public sealed record PlaceHoldResult(PlaceHoldStatus Status, Hold? Hold);

public enum TransitionStatus
{
    Applied,
    NotFound,
    NotActive
}

public sealed record TransitionResult(TransitionStatus Status, Hold? Hold);

/// <summary>
/// Persistence port for inventory and holds. Implementations must make stock deduction
/// and Active-state transitions atomic. Callers publish events only after a successful result.
/// </summary>
public interface IInventoryHoldStore
{
    Task<IReadOnlyList<Product>> GetAllProductsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<Product>> GetProductsByIdsAsync(
        IReadOnlyCollection<string> productIds,
        CancellationToken cancellationToken);

    Task SeedProductsIfEmptyAsync(IReadOnlyList<Product> products, CancellationToken cancellationToken);

    Task<PlaceHoldResult> PlaceHoldAsync(Hold hold, CancellationToken cancellationToken);

    Task<Hold?> GetHoldAsync(string holdId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Hold>> ListActiveUnexpiredAsync(DateTime utcNow, CancellationToken cancellationToken);

    Task<IReadOnlyList<Hold>> ListDueForExpiryAsync(DateTime utcNow, CancellationToken cancellationToken);

    Task<TransitionResult> TryTransitionAndRestoreAsync(
        string holdId,
        HoldStatus targetStatus,
        CancellationToken cancellationToken);
}
