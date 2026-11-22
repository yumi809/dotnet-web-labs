using System.Data;
using Microsoft.Extensions.Logging;
using Orders.Bll.Dto;
using Orders.Dal.Interfaces;
using Orders.Domain.Models;

namespace Orders.Bll.Events;

/// <summary>Порт публікації подій Замовлень (реалізація — Orders.Api/IntegrationEvents, RabbitMQ).</summary>
public interface IOrderEventsPublisher
{
    Task OrderCreatedAsync(OrderDto order, CancellationToken ct);
}

public interface IProductDirectorySync
{
    /// <summary>Застосовує ProductUpdated до локального довідника. false — подію вже оброблено.</summary>
    Task<bool> ApplyProductUpdatedAsync(Guid eventId, long productId, string name, decimal price, bool isActive, CancellationToken ct);
}

/// <summary>
/// Реплікація за політикою дублювання Лаб. 01: оновлюється ЛИШЕ локальний довідник dbo.Products;
/// ціни й назви в позиціях уже оформлених замовлень — знімок, їх не змінюємо.
/// Запис EventId у ProcessedEvents і upsert довідника — в одній транзакції UoW.
/// </summary>
public sealed class ProductDirectorySync(IUnitOfWork uow, ILogger<ProductDirectorySync> logger) : IProductDirectorySync
{
    public async Task<bool> ApplyProductUpdatedAsync(Guid eventId, long productId, string name, decimal price, bool isActive, CancellationToken ct)
    {
        await uow.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            if (!await uow.ProcessedEvents.TryMarkProcessedAsync(eventId, "ProductUpdatedIntegrationEvent", ct))
            {
                await uow.RollbackAsync(ct);
                logger.LogInformation("ProductUpdated {EventId} for product {ProductId} already processed — skipped", eventId, productId);
                return false;
            }

            await uow.Products.UpsertAsync(new ProductInfo { ProductId = productId, Name = name, Price = price, IsAvailable = isActive }, ct);
            await uow.CommitAsync(ct);
            logger.LogInformation("Product directory updated from event {EventId}: product {ProductId} '{ProductName}' price {Price}, available {IsAvailable}",
                eventId, productId, name, price, isActive);
            return true;
        }
        catch
        {
            await uow.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}
