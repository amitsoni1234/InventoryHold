namespace InventoryHold.Contracts;

public sealed class HoldResponse
{
    public required string HoldId { get; init; }

    public required string Status { get; init; }

    public required DateTime CreatedAtUtc { get; init; }

    public required DateTime ExpiresAtUtc { get; init; }

    public required IReadOnlyList<HoldItemResponse> Items { get; init; }
}

public sealed class HoldItemResponse
{
    public required string ProductId { get; init; }

    public required string ProductName { get; init; }

    public required int Quantity { get; init; }
}

public sealed class InventoryItemResponse
{
    public required string ProductId { get; init; }

    public required string Name { get; init; }

    public required int AvailableQuantity { get; init; }
}

public sealed class HoldLifecycleEvent
{
    public required string EventId { get; init; }

    public required string EventType { get; init; }

    public required DateTime OccurredAtUtc { get; init; }

    public required string HoldId { get; init; }

    public required string Status { get; init; }

    public required DateTime CreatedAtUtc { get; init; }

    public required DateTime ExpiresAtUtc { get; init; }

    public required IReadOnlyList<HoldItemResponse> Items { get; init; }
}
