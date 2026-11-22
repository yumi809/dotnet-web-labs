namespace Platform.EventBus.RabbitMq;

public sealed class EventBusOptions
{
    public const string SectionName = "EventBus";

    /// <summary>Durable topic exchange для всіх інтеграційних подій.</summary>
    public string ExchangeName { get; set; } = "bookhub.events";
    /// <summary>Dead letter exchange: сюди потрапляють відхилені (nack без requeue) повідомлення.</summary>
    public string DeadLetterExchangeName { get; set; } = "bookhub.events.dlx";
    /// <summary>Максимальна кількість повторів обробки (лічильник x-retry-count), після — DLQ.</summary>
    public int MaxRetryCount { get; set; } = 3;
    public ushort PrefetchCount { get; set; } = 10;
}

public static class EventHeaders
{
    public const string CorrelationId = "x-correlation-id";
    public const string RetryCount = "x-retry-count";
    public const string TraceParent = "traceparent";
}
