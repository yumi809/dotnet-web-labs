namespace Platform.EventBus.Abstractions;

/// <summary>
/// Базова інтеграційна подія: незмінний record, назва в минулому часі, самодостатні дані
/// (споживачу не потрібно звертатися до сервісу-власника).
/// </summary>
public abstract record IntegrationEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
    public string? CorrelationId { get; init; }

    /// <summary>Routing key у topic exchange, напр. "catalog.product.updated".</summary>
    public abstract string RoutingKey { get; }
}

/// <summary>Публікація інтеграційних подій.</summary>
public interface IEventPublisher
{
    Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken ct = default) where TEvent : IntegrationEvent;
}

/// <summary>Обробник конкретного типу події; виконується в окремому DI-scope повідомлення.</summary>
public interface IIntegrationEventHandler<in TEvent> where TEvent : IntegrationEvent
{
    Task HandleAsync(TEvent integrationEvent, CancellationToken ct);
}

/// <summary>Постійна помилка обробки (повтор не допоможе) — повідомлення одразу йде в DLQ.</summary>
public sealed class PermanentEventException(string message, Exception? inner = null) : Exception(message, inner);
