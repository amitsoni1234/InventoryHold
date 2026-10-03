using InventoryHold.Domain.Abstractions;
using InventoryHold.Domain.Entities;
using InventoryHold.Domain.Repositories;

namespace InventoryHold.Domain.Services;

public sealed class InventoryQueryService
{
    private readonly IInventoryHoldStore _store;
    private readonly IInventoryCache _cache;

    public InventoryQueryService(IInventoryHoldStore store, IInventoryCache cache)
    {
        _store = store;
        _cache = cache;
    }

    public async Task<IReadOnlyList<Product>> GetInventoryAsync(CancellationToken cancellationToken)
    {
        var cached = await _cache.GetInventoryAsync(cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var products = await _store.GetAllProductsAsync(cancellationToken);
        await _cache.SetInventoryAsync(products, cancellationToken);
        return products;
    }
}
