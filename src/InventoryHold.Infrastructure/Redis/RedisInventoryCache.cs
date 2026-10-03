using System.Text.Json;
using InventoryHold.Domain.Abstractions;
using InventoryHold.Domain.Entities;
using InventoryHold.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace InventoryHold.Infrastructure.Redis;

public sealed class RedisInventoryCache : IInventoryCache
{
    public const string Key = "inventory:catalog";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IConnectionMultiplexer _redis;
    private readonly TimeSpan _ttl;
    private readonly ILogger<RedisInventoryCache> _logger;

    public RedisInventoryCache(
        IConnectionMultiplexer redis,
        IOptions<RedisOptions> options,
        ILogger<RedisInventoryCache> logger)
    {
        _redis = redis;
        var seconds = options.Value.InventoryTtlSeconds <= 0 ? 10 : options.Value.InventoryTtlSeconds;
        _ttl = TimeSpan.FromSeconds(seconds);
        _logger = logger;
    }

    public async Task<IReadOnlyList<Product>?> GetInventoryAsync(CancellationToken cancellationToken)
    {
        try
        {
            var value = await _redis.GetDatabase().StringGetAsync(Key);
            if (value.IsNullOrEmpty)
            {
                return null;
            }

            var items = JsonSerializer.Deserialize<List<CachedProduct>>(value.ToString(), JsonOptions);
            return items?.Select(item => new Product(item.ProductId, item.Name, item.AvailableQuantity)).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis inventory read failed. MongoDB will be used.");
            return null;
        }
    }

    public async Task SetInventoryAsync(IReadOnlyList<Product> products, CancellationToken cancellationToken)
    {
        try
        {
            var payload = products.Select(product => new CachedProduct(product.ProductId, product.Name, product.AvailableQuantity)).ToList();
            var json = JsonSerializer.Serialize(payload, JsonOptions);
            await _redis.GetDatabase().StringSetAsync(Key, json, _ttl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis inventory write failed. The next read will use MongoDB.");
        }
    }

    public async Task InvalidateInventoryAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _redis.GetDatabase().KeyDeleteAsync(Key);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Redis inventory invalidation failed. Cached inventory may stay stale until the TTL elapses.");
        }
    }

    private sealed record CachedProduct(string ProductId, string Name, int AvailableQuantity);
}
