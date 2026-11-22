using Catalog.Bll.Caching;
using Catalog.Bll.Events;
using Catalog.Dal;
using Catalog.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Catalog.Bll.Services;

public interface IStockReservationService
{
    /// <summary>Резервує запас за подією OrderCreated. Повертає false, якщо подію вже оброблено.</summary>
    Task<bool> ReserveAsync(Guid eventId, string eventType, long orderId, IReadOnlyList<StockReservationLine> lines, CancellationToken ct);
}

/// <summary>
/// Ідемпотентний обробник бізнес-логіки: EventId записується в ProcessedEvents (унікальний ключ) і списання запасу
/// зберігаються ОДНИМ SaveChangesAsync — тобто в одній транзакції.
/// </summary>
public sealed class StockReservationService(IUnitOfWork uow, ICatalogCache cache, ILogger<StockReservationService> logger) : IStockReservationService
{
    public async Task<bool> ReserveAsync(Guid eventId, string eventType, long orderId, IReadOnlyList<StockReservationLine> lines, CancellationToken ct)
    {
        if (await uow.ProcessedEvents.ExistsAsync(eventId, ct))
        {
            logger.LogInformation("Event {EventId} ({EventType}) for order {OrderId} already processed — skipped", eventId, eventType, orderId);
            return false;
        }

        foreach (var line in lines)
        {
            var product = await uow.Products.GetByIdAsync(line.ProductId, ct)
                          ?? throw new DomainException($"Product {line.ProductId} from order {orderId} does not exist");
            product.RemoveStock(line.Quantity);   // DomainException, якщо запасу недостатньо
        }
        uow.ProcessedEvents.Add(eventId, eventType);

        try
        {
            await uow.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("PK_ProcessedEvents") == true)
        {
            // паралельний дубль тієї ж події — інша копія вже зберегла зміни
            logger.LogWarning("Concurrent duplicate of event {EventId} detected — skipped", eventId);
            return false;
        }

        foreach (var line in lines)
            await cache.RemoveAsync(CacheKeys.Product(line.ProductId), $"stock reserved for order {orderId}", ct);
        logger.LogInformation("Stock reserved for order {OrderId}: {LinesCount} lines (event {EventId})", orderId, lines.Count, eventId);
        return true;
    }
}
