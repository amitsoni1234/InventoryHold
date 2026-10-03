using InventoryHold.Domain.Abstractions;
using InventoryHold.Infrastructure.Messaging;
using InventoryHold.Infrastructure.Mongo;
using InventoryHold.Infrastructure.Options;
using InventoryHold.Infrastructure.Redis;
using InventoryHold.Infrastructure.Seeding;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using StackExchange.Redis;

namespace InventoryHold.Infrastructure.DependencyInjection;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInventoryInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MongoOptions>(configuration.GetSection(MongoOptions.SectionName));
        services.Configure<RedisOptions>(configuration.GetSection(RedisOptions.SectionName));
        services.Configure<RabbitMqOptions>(configuration.GetSection(RabbitMqOptions.SectionName));

        services.AddSingleton<IMongoClient>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<MongoOptions>>().Value;
            if (string.IsNullOrWhiteSpace(options.ConnectionString))
            {
                throw new InvalidOperationException("Mongo:ConnectionString is required.");
            }

            return new MongoClient(options.ConnectionString);
        });

        services.AddSingleton<MongoInventoryHoldStore>();
        services.AddSingleton<Domain.Repositories.IInventoryHoldStore>(sp => sp.GetRequiredService<MongoInventoryHoldStore>());

        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<RedisOptions>>().Value;
            if (string.IsNullOrWhiteSpace(options.ConnectionString))
            {
                throw new InvalidOperationException("Redis:ConnectionString is required.");
            }

            var configurationOptions = ConfigurationOptions.Parse(options.ConnectionString);
            configurationOptions.AbortOnConnectFail = false;
            return ConnectionMultiplexer.Connect(configurationOptions);
        });

        services.AddSingleton<IInventoryCache, RedisInventoryCache>();
        services.AddSingleton<RabbitMqHoldEventPublisher>();
        services.AddSingleton<IHoldEventPublisher>(sp => sp.GetRequiredService<RabbitMqHoldEventPublisher>());
        services.AddHostedService<InventoryBootstrapHostedService>();
        return services;
    }
}

public sealed class InventoryBootstrapHostedService : IHostedService
{
    private readonly MongoInventoryHoldStore _store;
    private readonly RabbitMqHoldEventPublisher _publisher;
    private readonly ILogger<InventoryBootstrapHostedService> _logger;

    public InventoryBootstrapHostedService(
        MongoInventoryHoldStore store,
        RabbitMqHoldEventPublisher publisher,
        ILogger<InventoryBootstrapHostedService> logger)
    {
        _store = store;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _store.EnsureReadyAsync(cancellationToken);
        await _store.SeedProductsIfEmptyAsync(ProductCatalog.SeedProducts, cancellationToken);
        try
        {
            await _publisher.EnsureTopologyAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RabbitMQ topology was not declared at startup. Publish will retry later.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
