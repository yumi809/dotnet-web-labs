using Catalog.Bll.Dto;
using Catalog.Bll.Events;
using Platform.EventBus.Abstractions;
using Platform.EventBus.Events;
using Platform.ServiceDefaults.Correlation;

namespace Catalog.Api.IntegrationEvents;

/// <summary>Адаптер порту IProductEventsPublisher на RabbitMQ: формує самодостатні події з CorrelationId запиту.</summary>
public sealed class ProductEventsPublisher(IEventPublisher publisher, ICorrelationIdAccessor correlation) : IProductEventsPublisher
{
    public async Task ProductChangedAsync(ProductDto product, decimal? oldPrice, CancellationToken ct)
    {
        await publisher.PublishAsync(new ProductUpdatedIntegrationEvent(product.Id, product.Name, product.Price, product.IsActive)
        {
            CorrelationId = correlation.CorrelationId
        }, ct);

        if (oldPrice is { } previous && previous != product.Price)
        {
            await publisher.PublishAsync(new ProductPriceChangedIntegrationEvent(product.Id, product.Name, previous, product.Price)
            {
                CorrelationId = correlation.CorrelationId
            }, ct);
        }
    }
}
