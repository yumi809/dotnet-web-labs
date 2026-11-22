using Orders.Bll.Events;
using Platform.EventBus.Abstractions;
using Platform.EventBus.Events;

namespace Orders.Api.IntegrationEvents;

/// <summary>
/// Обробник ProductUpdated (окремий обробник на тип події, виконується в scope повідомлення):
/// оновлює локальний довідник товарів. Ціни в уже оформлених позиціях не змінюються (знімок).
/// </summary>
public sealed class ProductUpdatedHandler(IProductDirectorySync sync, ILogger<ProductUpdatedHandler> logger)
    : IIntegrationEventHandler<ProductUpdatedIntegrationEvent>
{
    public async Task HandleAsync(ProductUpdatedIntegrationEvent e, CancellationToken ct)
    {
        if (e.ProductId <= 0 || string.IsNullOrWhiteSpace(e.Name) || e.Price < 0)
            throw new PermanentEventException($"Invalid ProductUpdated payload for product {e.ProductId}");

        var applied = await sync.ApplyProductUpdatedAsync(e.EventId, e.ProductId, e.Name, e.Price, e.IsActive, ct);
        logger.LogInformation("ProductUpdated {EventId} for product {ProductId} {Result}, correlation {CorrelationId}",
            e.EventId, e.ProductId, applied ? "applied" : "skipped as duplicate", e.CorrelationId);
    }
}
