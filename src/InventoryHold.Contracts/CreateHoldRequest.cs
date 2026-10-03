namespace InventoryHold.Contracts;

public sealed class CreateHoldRequest
{
    public List<CreateHoldItemRequest> Items { get; set; } = [];

    /// <summary>
    /// Optional caller key. Replaying the same key returns the original hold and does not deduct stock again.
    /// </summary>
    public string? ClientRequestId { get; set; }
}

public sealed class CreateHoldItemRequest
{
    public string ProductId { get; set; } = "";

    public int Quantity { get; set; }
}
