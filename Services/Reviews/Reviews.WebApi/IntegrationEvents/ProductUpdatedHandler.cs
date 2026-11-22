using MediatR;
using Platform.EventBus.Abstractions;
using Platform.EventBus.Events;
using Reviews.Application.Commands;

namespace Reviews.WebApi.IntegrationEvents;

/// <summary>Обробник ProductUpdated: оновлює денормалізовану назву товару в усіх відгуках і обговореннях.</summary>
public sealed class ProductUpdatedHandler(ISender sender, ILogger<ProductUpdatedHandler> logger)
    : IIntegrationEventHandler<ProductUpdatedIntegrationEvent>
{
    public async Task HandleAsync(ProductUpdatedIntegrationEvent e, CancellationToken ct)
    {
        if (e.ProductId <= 0 || string.IsNullOrWhiteSpace(e.Name))
            throw new PermanentEventException($"Invalid ProductUpdated payload for product {e.ProductId}");

        var applied = await sender.Send(new SyncProductNameCommand(e.EventId, e.ProductId, e.Name), ct);
        logger.LogInformation("ProductUpdated {EventId} for product {ProductId} {Result}, correlation {CorrelationId}",
            e.EventId, e.ProductId, applied ? "applied" : "skipped as duplicate", e.CorrelationId);
    }
}
