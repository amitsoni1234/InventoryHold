using InventoryHold.Domain.Entities;

namespace InventoryHold.Domain.Abstractions;

public interface IClock
{
    DateTime UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}

public interface IHoldDurationProvider
{
    TimeSpan Value { get; }
}

public interface IInventoryCache
{
    Task<IReadOnlyList<Product>?> GetInventoryAsync(CancellationToken cancellationToken);

    Task SetInventoryAsync(IReadOnlyList<Product> products, CancellationToken cancellationToken);

    Task InvalidateInventoryAsync(CancellationToken cancellationToken);
}

public interface IHoldEventPublisher
{
    Task PublishCreatedAsync(Hold hold, CancellationToken cancellationToken);

    Task PublishReleasedAsync(Hold hold, CancellationToken cancellationToken);

    Task PublishExpiredAsync(Hold hold, CancellationToken cancellationToken);
}
