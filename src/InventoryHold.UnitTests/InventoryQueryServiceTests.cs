using InventoryHold.Domain.Entities;
using InventoryHold.Domain.Services;
using InventoryHold.UnitTests.Fakes;

namespace InventoryHold.UnitTests;

public class InventoryQueryServiceTests
{
    [Fact]
    public async Task GetInventory_ReturnsCacheWithoutReadingStore()
    {
        var store = new FakeStore();
        store.AddProduct("mouse", "Wireless Mouse", 10);
        var cache = new FakeCache
        {
            Cached = [new Product("mouse", "Wireless Mouse", 3)]
        };
        var service = new InventoryQueryService(store, cache);

        var products = await service.GetInventoryAsync(CancellationToken.None);

        Assert.Equal(3, products.Single().AvailableQuantity);
        Assert.Equal(1, cache.Reads);
        Assert.Equal(0, cache.Writes);
    }

    [Fact]
    public async Task GetInventory_LoadsStoreAndCachesOnMiss()
    {
        var store = new FakeStore();
        store.AddProduct("mouse", "Wireless Mouse", 10);
        var cache = new FakeCache();
        var service = new InventoryQueryService(store, cache);

        var products = await service.GetInventoryAsync(CancellationToken.None);

        Assert.Equal(10, products.Single().AvailableQuantity);
        Assert.Equal(1, cache.Writes);
        Assert.NotNull(cache.Cached);
    }
}
