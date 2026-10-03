using InventoryHold.Domain.Services;
using InventoryHold.WebApi.Configuration;

namespace InventoryHold.WebApi.Hosting;

public sealed class HoldExpirationWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<HoldExpirationWorker> _logger;

    public HoldExpirationWorker(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<HoldExpirationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var seconds = _configuration.GetValue<int?>($"{HoldSettings.SectionName}:ExpirationPollSeconds") ?? 15;
        var delay = TimeSpan.FromSeconds(Math.Max(1, seconds));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var holds = scope.ServiceProvider.GetRequiredService<HoldService>();
                await holds.ExpireDueHoldsAsync(stoppingToken);
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Hold expiration sweep failed.");
            }
        }
    }
}
