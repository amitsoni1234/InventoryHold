using InventoryHold.Domain.Entities;

namespace InventoryHold.Infrastructure.Seeding;

public static class ProductCatalog
{
    public static IReadOnlyList<Product> SeedProducts { get; } =
    [
        new("prod-wireless-mouse", "Wireless Mouse", 50),
        new("prod-mechanical-keyboard", "Mechanical Keyboard", 30),
        new("prod-usb-c-hub", "USB-C Hub", 40),
        new("prod-monitor-27", "27-inch Monitor", 15),
        new("prod-laptop-stand", "Laptop Stand", 25)
    ];
}
