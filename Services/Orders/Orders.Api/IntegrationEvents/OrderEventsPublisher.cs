using Orders.Bll.Dto;
using Orders.Bll.Events;
using Platform.EventBus.Abstractions;
using Platform.EventBus.Events;
using Platform.ServiceDefaults.Correlation;

namespace Orders.Api.IntegrationEvents;

/// <summary>Публікує OrderCreated — самодостатня подія з позиціями (Каталогу не треба звертатися до Замовлень).</summary>
public sealed class OrderEventsPublisher(IEventPublisher publisher, ICorrelationIdAccessor correlation) : IOrderEventsPublisher
{
    public Task OrderCreatedAsync(OrderDto order, CancellationToken ct) =>
        publisher.PublishAsync(new OrderCreatedIntegrationEvent(
            order.Id, order.OrderNumber, order.CustomerId,
            order.Items.Select(i => new OrderLineSnapshot(i.ProductId, i.Quantity)).ToList())
        {
            CorrelationId = correlation.CorrelationId
        }, ct);
}
