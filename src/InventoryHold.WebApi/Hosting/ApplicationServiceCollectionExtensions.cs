using InventoryHold.Domain;
using InventoryHold.Domain.Abstractions;
using InventoryHold.Domain.Services;
using InventoryHold.WebApi.Configuration;

namespace InventoryHold.WebApi.Hosting;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddHoldApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<HoldSettings>(configuration.GetSection(HoldSettings.SectionName));
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IHoldDurationProvider, ConfiguredHoldDuration>();
        services.AddSingleton<HoldService>();
        services.AddSingleton<InventoryQueryService>();
        services.AddHostedService<HoldExpirationWorker>();
        return services;
    }
}
