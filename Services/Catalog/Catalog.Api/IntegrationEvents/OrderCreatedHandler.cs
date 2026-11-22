using Catalog.Bll.Events;
using Catalog.Bll.Services;
using Catalog.Domain.Exceptions;
using Platform.EventBus.Abstractions;
using Platform.EventBus.Events;

namespace Catalog.Api.IntegrationEvents;

/// <summary>
/// Обробник OrderCreated: резервує запас. Ідемпотентність — таблиця ProcessedEvents (міграція AddProcessedEvents).
/// Порушення бізнес-правила (немає товару, недостатньо запасу) — постійна помилка → DLQ без повторів.
/// </summary>
public sealed class OrderCreatedHandler(IStockReservationService reservations, ILogger<OrderCreatedHandler> logger)
    : IIntegrationEventHandler<OrderCreatedIntegrationEvent>
{
    public async Task HandleAsync(OrderCreatedIntegrationEvent e, CancellationToken ct)
    {
        try
        {
            var applied = await reservations.ReserveAsync(e.EventId, nameof(OrderCreatedIntegrationEvent), e.OrderId,
                e.Lines.Select(l => new StockReservationLine(l.ProductId, l.Quantity)).ToList(), ct);
            logger.LogInformation("OrderCreated {EventId} for order {OrderId} ({OrderNumber}) {Result}, correlation {CorrelationId}",
                e.EventId, e.OrderId, e.OrderNumber, applied ? "applied" : "skipped as duplicate", e.CorrelationId);
        }
        catch (DomainException ex)
        {
            logger.LogError("Stock reservation for order {OrderId} failed: {Reason} (event {EventId}, correlation {CorrelationId})",
                e.OrderId, ex.Message, e.EventId, e.CorrelationId);
            throw new PermanentEventException(ex.Message, ex);
        }
    }
}
