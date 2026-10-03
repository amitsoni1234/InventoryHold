using InventoryHold.Contracts;
using InventoryHold.Domain.Entities;

namespace InventoryHold.WebApi.Mapping;

public static class ResponseMapper
{
    public static HoldResponse ToResponse(Hold hold) =>
        new()
        {
            HoldId = hold.HoldId,
            Status = hold.Status.ToString(),
            CreatedAtUtc = hold.CreatedAtUtc,
            ExpiresAtUtc = hold.ExpiresAtUtc,
            Items = hold.Items.Select(ToResponse).ToList()
        };

    public static HoldItemResponse ToResponse(HoldItem item) =>
        new()
        {
            ProductId = item.ProductId,
            ProductName = item.ProductName,
            Quantity = item.Quantity
        };

    public static InventoryItemResponse ToResponse(Product product) =>
        new()
        {
            ProductId = product.ProductId,
            Name = product.Name,
            AvailableQuantity = product.AvailableQuantity
        };
}
