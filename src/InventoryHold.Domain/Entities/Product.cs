namespace InventoryHold.Domain.Entities;

public sealed class Product
{
    public Product(string productId, string name, int availableQuantity)
    {
        if (string.IsNullOrWhiteSpace(productId))
        {
            throw new ArgumentException("Product id is required.", nameof(productId));
        }

        ProductId = productId.Trim();
        Name = name ?? string.Empty;
        AvailableQuantity = availableQuantity;
    }

    public string ProductId { get; }

    public string Name { get; }

    public int AvailableQuantity { get; }
}
