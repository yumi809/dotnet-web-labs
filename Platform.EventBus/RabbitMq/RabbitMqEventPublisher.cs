using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Platform.EventBus.Abstractions;
using RabbitMQ.Client;

namespace Platform.EventBus.RabbitMq;

/// <summary>
/// Публікація подій: camelCase JSON, BasicProperties (Persistent, ContentType, MessageId = EventId, Type),
/// заголовки x-correlation-id і W3C traceparent; логування кожної публікації.
/// </summary>
public sealed class RabbitMqEventPublisher(
    IConnection connection,
    IOptions<EventBusOptions> options,
    ILogger<RabbitMqEventPublisher> logger) : IEventPublisher, IAsyncDisposable
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web); // camelCase

    private readonly SemaphoreSlim _lock = new(1, 1);
    private IChannel? _channel;

    public async Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken ct = default) where TEvent : IntegrationEvent
    {
        var eventType = typeof(TEvent).Name;
        var exchange = options.Value.ExchangeName;

        using var activity = EventBusTelemetry.ActivitySource.StartActivity($"{integrationEvent.RoutingKey} publish", ActivityKind.Producer);
        EventBusTelemetry.SetMessagingTags(activity, "publish", exchange, integrationEvent.EventId.ToString(), eventType);

        var headers = new Dictionary<string, object?>
        {
            [EventHeaders.CorrelationId] = integrationEvent.CorrelationId ?? string.Empty
        };
        EventBusTelemetry.Inject(activity, headers);

        var properties = new BasicProperties
        {
            Persistent = true,                                   // переживає перезапуск брокера (черги durable)
            ContentType = "application/json",
            MessageId = integrationEvent.EventId.ToString(),
            Type = eventType,
            CorrelationId = integrationEvent.CorrelationId,
            Timestamp = new AmqpTimestamp(new DateTimeOffset(integrationEvent.OccurredAt).ToUnixTimeSeconds()),
            Headers = headers
        };
        var body = JsonSerializer.SerializeToUtf8Bytes(integrationEvent, integrationEvent.GetType(), Json);

        await _lock.WaitAsync(ct);
        try
        {
            var channel = await GetChannelAsync(ct);
            await channel.BasicPublishAsync(exchange, integrationEvent.RoutingKey, mandatory: false, properties, body, ct);
        }
        finally
        {
            _lock.Release();
        }

        logger.LogInformation("Published {EventType} {EventId} to {Exchange} with routing key {RoutingKey}, correlation {CorrelationId}",
            eventType, integrationEvent.EventId, exchange, integrationEvent.RoutingKey, integrationEvent.CorrelationId);
    }

    private async Task<IChannel> GetChannelAsync(CancellationToken ct)
    {
        if (_channel is { IsOpen: true }) return _channel;
        _channel = await connection.CreateChannelAsync(cancellationToken: ct);
        await RabbitMqTopology.DeclareExchangesAsync(_channel, options.Value, ct);
        return _channel;
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null) await _channel.DisposeAsync();
        _lock.Dispose();
    }
}
