using MediatR;
using Microsoft.Extensions.Logging;
using Reviews.Application.Abstractions;
using Reviews.Domain.Interfaces;

namespace Reviews.Application.Commands;

/// <summary>Сховище оброблених подій (ідемпотентність споживача). Реалізація — колекція processedEvents у MongoDB.</summary>
public interface IProcessedEventStore
{
    /// <summary>Атомарно позначає подію обробленою; false — EventId вже є (дубль).</summary>
    Task<bool> TryMarkProcessedAsync(Guid eventId, string eventType, CancellationToken ct);
    Task UnmarkAsync(Guid eventId, CancellationToken ct);
}

/// <summary>
/// Реплікація за політикою дублювання Лаб. 01: денормалізована назва товару оновлюється в УСІХ документах
/// товару (reviews.product.productName, discussions.product.productName).
/// </summary>
public sealed record SyncProductNameCommand(Guid EventId, long ProductId, string ProductName) : ICommand<bool>;

public sealed class SyncProductNameHandler(
    IProcessedEventStore processed,
    IReviewRepository reviews,
    IDiscussionRepository discussions,
    ILogger<SyncProductNameHandler> logger) : IRequestHandler<SyncProductNameCommand, bool>
{
    public async Task<bool> Handle(SyncProductNameCommand c, CancellationToken ct)
    {
        // Маркер вставляється першим (унікальний _id = EventId): паралельний дубль отримає DuplicateKey і буде пропущений
        if (!await processed.TryMarkProcessedAsync(c.EventId, "ProductUpdatedIntegrationEvent", ct))
        {
            logger.LogInformation("ProductUpdated {EventId} for product {ProductId} already processed — skipped", c.EventId, c.ProductId);
            return false;
        }

        try
        {
            var reviewsUpdated = await reviews.RenameProductAsync(c.ProductId, c.ProductName, ct);
            var discussionsUpdated = await discussions.RenameProductAsync(c.ProductId, c.ProductName, ct);
            logger.LogInformation("Product {ProductId} renamed to '{ProductName}' in {Reviews} reviews and {Discussions} discussions (event {EventId})",
                c.ProductId, c.ProductName, reviewsUpdated, discussionsUpdated, c.EventId);
            return true;
        }
        catch
        {
            // компенсація: прибираємо маркер, щоб повторна доставка застосувала зміни
            await processed.UnmarkAsync(c.EventId, CancellationToken.None);
            throw;
        }
    }
}
