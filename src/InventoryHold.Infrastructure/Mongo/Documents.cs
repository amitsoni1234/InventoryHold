using InventoryHold.Domain;
using InventoryHold.Domain.Entities;
using MongoDB.Bson.Serialization.Attributes;

namespace InventoryHold.Infrastructure.Mongo;

public sealed class ProductDocument
{
    [BsonId]
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    public int AvailableQuantity { get; set; }
}

public sealed class HoldItemDocument
{
    public string ProductId { get; set; } = "";

    public string ProductName { get; set; } = "";

    public int Quantity { get; set; }
}

public sealed class HoldDocument
{
    [BsonId]
    public string Id { get; set; } = "";

    public string Status { get; set; } = "";

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime CreatedAtUtc { get; set; }

    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime ExpiresAtUtc { get; set; }

    [BsonIgnoreIfNull]
    public string? ClientRequestId { get; set; }

    public List<HoldItemDocument> Items { get; set; } = [];
}

internal static class DocumentMapper
{
    public static Product ToProduct(ProductDocument document) =>
        new(document.Id, document.Name, document.AvailableQuantity);

    public static ProductDocument ToDocument(Product product) =>
        new()
        {
            Id = product.ProductId,
            Name = product.Name,
            AvailableQuantity = product.AvailableQuantity
        };

    public static Hold ToHold(HoldDocument document) =>
        new(
            document.Id,
            Enum.Parse<HoldStatus>(document.Status),
            document.CreatedAtUtc,
            document.ExpiresAtUtc,
            document.Items.Select(item => new HoldItem(item.ProductId, item.ProductName, item.Quantity)).ToList(),
            document.ClientRequestId);

    public static HoldDocument ToDocument(Hold hold) =>
        new()
        {
            Id = hold.HoldId,
            Status = hold.Status.ToString(),
            CreatedAtUtc = hold.CreatedAtUtc,
            ExpiresAtUtc = hold.ExpiresAtUtc,
            ClientRequestId = hold.ClientRequestId,
            Items = hold.Items.Select(item => new HoldItemDocument
            {
                ProductId = item.ProductId,
                ProductName = item.ProductName,
                Quantity = item.Quantity
            }).ToList()
        };
}
