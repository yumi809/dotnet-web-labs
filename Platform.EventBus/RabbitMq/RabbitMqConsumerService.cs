using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Platform.EventBus.Abstractions;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Serilog.Context;

namespace Platform.EventBus.RabbitMq;

/// <summary>Реєстрація споживача: черга сервісу і типи подій, на які він підписаний.</summary>
public sealed class ConsumerRegistration
{
    public required string QueueName { get; init; }
    public Dictionary<string, (Type EventType, string RoutingKey)> Subscriptions { get; } = new();
}

/// <summary>
/// Consumer як BackgroundService з РУЧНИМ підтвердженням (autoAck: false):
/// <list type="bullet">
/// <item>успіх → BasicAck;</item>
/// <item>некоректний JSON / невідомий тип / PermanentEventException → BasicNack(requeue: false) → DLQ;</item>
/// <item>тимчасова помилка → перепублікація в ту ж чергу з x-retry-count + 1 і Ack оригіналу; після MaxRetryCount → DLQ.</item>
/// </list>
/// Кожне повідомлення обробляється в окремому DI-scope; контекст трасування витягується з заголовків.
/// </summary>
public sealed class RabbitMqConsumerService(
    IConnection connection,
    IServiceScopeFactory scopeFactory,
    ConsumerRegistration registration,
    IOptions<EventBusOptions> options,
    ILogger<RabbitMqConsumerService> logger) : BackgroundService
{
    private IChannel? _channel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var opts = options.Value;
        _channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
        await RabbitMqTopology.DeclareConsumerQueueAsync(
            _channel, opts, registration.QueueName, registration.Subscriptions.Values.Select(s => s.RoutingKey).Distinct(), stoppingToken);
        await _channel.BasicQosAsync(prefetchSize: 0, prefetchCount: opts.PrefetchCount, global: false, cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += (_, ea) => OnMessageAsync(ea, stoppingToken);

        var tag = await _channel.BasicConsumeAsync(registration.QueueName, autoAck: false, consumer, stoppingToken);
        logger.LogInformation("Consumer {ConsumerTag} started on queue {Queue} for {EventTypes}",
            tag, registration.QueueName, string.Join(", ", registration.Subscriptions.Keys));

        // коректна зупинка: чекаємо на stoppingToken, потім закриваємо канал
        try { await Task.Delay(Timeout.Infinite, stoppingToken); }
        catch (OperationCanceledException) { }
        await _channel.CloseAsync(CancellationToken.None);
        logger.LogInformation("Consumer on queue {Queue} stopped", registration.QueueName);
    }

    private async Task OnMessageAsync(BasicDeliverEventArgs ea, CancellationToken ct)
    {
        var channel = _channel!;
        var props = ea.BasicProperties;
        var eventType = props.Type ?? "unknown";
        var messageId = props.MessageId;
        var headers = props.Headers;
        var correlationId = ReadHeader(headers, EventHeaders.CorrelationId);
        var retryCount = int.TryParse(ReadHeader(headers, EventHeaders.RetryCount), out var r) ? r : 0;

        var parent = EventBusTelemetry.Extract(headers);
        using var activity = EventBusTelemetry.ActivitySource.StartActivity(
            $"{registration.QueueName} process", ActivityKind.Consumer, parent.ActivityContext);
        EventBusTelemetry.SetMessagingTags(activity, "process", registration.QueueName, messageId, eventType);
        activity?.SetTag("messaging.rabbitmq.retry_count", retryCount);

        using var _ = LogContext.PushProperty("CorrelationId", correlationId);
        using var __ = LogContext.PushProperty("EventId", messageId);
        using var ___ = LogContext.PushProperty("EventType", eventType);

        // 1. Невідомий тип або некоректний JSON — постійна помилка, повтор не допоможе → DLQ
        if (!registration.Subscriptions.TryGetValue(eventType, out var subscription))
        {
            logger.LogError("Unknown event type {EventType} ({EventId}) on {Queue} — rejected to DLQ", eventType, messageId, registration.QueueName);
            await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, ct);
            return;
        }

        IntegrationEvent? integrationEvent;
        try
        {
            integrationEvent = (IntegrationEvent?)JsonSerializer.Deserialize(ea.Body.Span, subscription.EventType, RabbitMqEventPublisher.Json);
            if (integrationEvent is null) throw new JsonException("Empty payload");
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Malformed {EventType} {EventId} — rejected to DLQ", eventType, messageId);
            await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, ct);
            return;
        }

        // 2. Обробка в окремому scope окремим обробником типу події
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var handlerType = typeof(IIntegrationEventHandler<>).MakeGenericType(subscription.EventType);
            var handler = scope.ServiceProvider.GetRequiredService(handlerType);
            var handle = handlerType.GetMethod(nameof(IIntegrationEventHandler<IntegrationEvent>.HandleAsync))!;
            try
            {
                await (Task)handle.Invoke(handler, [integrationEvent, ct])!;
            }
            catch (System.Reflection.TargetInvocationException tie) when (tie.InnerException is not null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(tie.InnerException).Throw();
            }

            await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, ct);
            logger.LogInformation("Handled {EventType} {EventId} from {Queue}", eventType, messageId, registration.QueueName);
        }
        catch (PermanentEventException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            logger.LogError(ex, "Permanent failure handling {EventType} {EventId} — rejected to DLQ", eventType, messageId);
            await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            await RetryOrDeadLetterAsync(channel, ea, retryCount, ex, ct);
        }
    }

    /// <summary>Повтор: перепублікація в ту ж чергу зі збільшеним x-retry-count, оригінал підтверджується; після максимуму → DLQ.</summary>
    private async Task RetryOrDeadLetterAsync(IChannel channel, BasicDeliverEventArgs ea, int retryCount, Exception ex, CancellationToken ct)
    {
        var props = ea.BasicProperties;
        if (retryCount >= options.Value.MaxRetryCount)
        {
            logger.LogError(ex, "Handling {EventType} {EventId} failed after {RetryCount} retries — moved to DLQ", props.Type, props.MessageId, retryCount);
            await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, ct);
            return;
        }

        var headers = props.Headers is null ? new Dictionary<string, object?>() : new Dictionary<string, object?>(props.Headers);
        headers[EventHeaders.RetryCount] = (retryCount + 1).ToString();
        var retryProps = new BasicProperties
        {
            Persistent = true,
            ContentType = props.ContentType,
            MessageId = props.MessageId,
            Type = props.Type,
            CorrelationId = props.CorrelationId,
            Headers = headers
        };

        // default exchange ("") маршрутизує прямо в чергу за її ім'ям
        await channel.BasicPublishAsync(string.Empty, registration.QueueName, mandatory: false, retryProps, ea.Body, ct);
        await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, ct);
        logger.LogWarning(ex, "Handling {EventType} {EventId} failed, retry {Retry}/{MaxRetry} scheduled",
            props.Type, props.MessageId, retryCount + 1, options.Value.MaxRetryCount);
    }

    private static string? ReadHeader(IDictionary<string, object?>? headers, string key) =>
        headers is not null && headers.TryGetValue(key, out var value) && value is not null
            ? value is byte[] bytes ? Encoding.UTF8.GetString(bytes) : value.ToString()
            : null;

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        if (_channel is not null) await _channel.DisposeAsync();
    }
}
