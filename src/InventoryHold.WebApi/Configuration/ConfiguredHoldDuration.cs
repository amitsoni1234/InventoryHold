using InventoryHold.Domain.Abstractions;
using InventoryHold.WebApi.Configuration;
using Microsoft.Extensions.Options;

namespace InventoryHold.WebApi.Configuration;

public sealed class ConfiguredHoldDuration : IHoldDurationProvider
{
    public ConfiguredHoldDuration(IOptions<HoldSettings> options)
    {
        var minutes = options.Value.DurationMinutes <= 0 ? 15 : options.Value.DurationMinutes;
        Value = TimeSpan.FromMinutes(minutes);
    }

    public TimeSpan Value { get; }
}
