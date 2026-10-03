namespace InventoryHold.Domain.Entities;

public sealed class HoldItem
{
    public HoldItem(string productId, string productName, int quantity)
    {
        if (string.IsNullOrWhiteSpace(productId))
        {
            throw new ArgumentException("Product id is required.", nameof(productId));
        }

        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be positive.");
        }

        ProductId = productId.Trim();
        ProductName = productName ?? string.Empty;
        Quantity = quantity;
    }

    public string ProductId { get; }

    public string ProductName { get; }

    public int Quantity { get; }
}
