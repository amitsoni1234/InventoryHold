using System.Text;
using System.Text.Json;
using InventoryHold.Contracts;
using InventoryHold.Domain.Abstractions;
using InventoryHold.Domain.Entities;
using InventoryHold.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace InventoryHold.Infrastructure.Messaging;

public sealed class RabbitMqHoldEventPublisher : IHoldEventPublisher, IAsyncDisposable
{
    public const string CreatedRoutingKey = "hold.created";
    public const string ReleasedRoutingKey = "hold.released";
    public const string ExpiredRoutingKey = "hold.expired";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly RabbitMqOptions _options;
    private readonly IClock _clock;
    private readonly ILogger<RabbitMqHoldEventPublisher> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConnection? _connection;
    private IChannel? _channel;

    public RabbitMqHoldEventPublisher(
        IOptions<RabbitMqOptions> options,
        IClock clock,
        ILogger<RabbitMqHoldEventPublisher> logger)
    {
        _options = options.Value;
        _clock = clock;
        _logger = logger;
    }

    public Task PublishCreatedAsync(Hold hold, CancellationToken cancellationToken) =>
        PublishAsync("HoldCreated", CreatedRoutingKey, hold, cancellationToken);

    public Task PublishReleasedAsync(Hold hold, CancellationToken cancellationToken) =>
        PublishAsync("HoldReleased", ReleasedRoutingKey, hold, cancellationToken);

    public Task PublishExpiredAsync(Hold hold, CancellationToken cancellationToken) =>
        PublishAsync("HoldExpired", ExpiredRoutingKey, hold, cancellationToken);

    public async Task EnsureTopologyAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureChannelAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_channel is not null)
            {
                await _channel.DisposeAsync();
            }

            if (_connection is not null)
            {
                await _connection.DisposeAsync();
            }
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    private async Task PublishAsync(string eventType, string routingKey, Hold hold, CancellationToken cancellationToken)
    {
        var lifecycleEvent = new HoldLifecycleEvent
        {
            EventId = Guid.NewGuid().ToString("D"),
            EventType = eventType,
            OccurredAtUtc = _clock.UtcNow,
            HoldId = hold.HoldId,
            Status = hold.Status.ToString(),
            CreatedAtUtc = hold.CreatedAtUtc,
            ExpiresAtUtc = hold.ExpiresAtUtc,
            Items = hold.Items.Select(item => new HoldItemResponse
            {
                ProductId = item.ProductId,
                ProductName = item.ProductName,
                Quantity = item.Quantity
            }).ToList()
        };

        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(lifecycleEvent, JsonOptions));
        await _gate.WaitAsync(cancellationToken);
        try
        {
            const int maxAttempts = 3;
            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    var channel = await EnsureChannelAsync(cancellationToken);
                    var properties = new BasicProperties
                    {
                        Persistent = true,
                        ContentType = "application/json",
                        MessageId = lifecycleEvent.EventId,
                        Type = eventType
                    };
                    await channel.BasicPublishAsync(
                        exchange: _options.Exchange,
                        routingKey: routingKey,
                        mandatory: false,
                        basicProperties: properties,
                        body: body,
                        cancellationToken: cancellationToken);
                    return;
                }
                catch (Exception ex) when (attempt < maxAttempts)
                {
                    _logger.LogWarning(ex, "RabbitMQ publish failed for {EventType}. Retrying.", eventType);
                    await ResetChannelAsync();
                    await Task.Delay(TimeSpan.FromMilliseconds(100 * attempt), cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Failed to publish {EventType} for hold {HoldId} after persistence. The hold change was kept.",
                        eventType,
                        hold.HoldId);
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IChannel> EnsureChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel is { IsOpen: true })
        {
            return _channel;
        }

        await ResetChannelAsync();
        var factory = new ConnectionFactory
        {
            HostName = _options.Host,
            Port = _options.Port,
            UserName = _options.Username,
            Password = _options.Password,
            AutomaticRecoveryEnabled = true
        };
        _connection = await factory.CreateConnectionAsync(cancellationToken);
        _channel = await _connection.CreateChannelAsync(cancellationToken: cancellationToken);

        var exchange = string.IsNullOrWhiteSpace(_options.Exchange) ? "inventory.holds" : _options.Exchange;
        await _channel.ExchangeDeclareAsync(
            exchange: exchange,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            cancellationToken: cancellationToken);

        await DeclareQueueAsync(_channel, "inventory.holds.created", CreatedRoutingKey, exchange, cancellationToken);
        await DeclareQueueAsync(_channel, "inventory.holds.released", ReleasedRoutingKey, exchange, cancellationToken);
        await DeclareQueueAsync(_channel, "inventory.holds.expired", ExpiredRoutingKey, exchange, cancellationToken);
        return _channel;
    }

    private static async Task DeclareQueueAsync(
        IChannel channel,
        string queue,
        string routingKey,
        string exchange,
        CancellationToken cancellationToken)
    {
        await channel.QueueDeclareAsync(
            queue: queue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);
        await channel.QueueBindAsync(
            queue: queue,
            exchange: exchange,
            routingKey: routingKey,
            cancellationToken: cancellationToken);
    }

    private async Task ResetChannelAsync()
    {
        if (_channel is not null)
        {
            await _channel.DisposeAsync();
            _channel = null;
        }

        if (_connection is not null)
        {
            await _connection.DisposeAsync();
            _connection = null;
        }
    }
}
