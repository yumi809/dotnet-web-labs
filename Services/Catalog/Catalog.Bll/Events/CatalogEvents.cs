using Catalog.Bll.Dto;

namespace Catalog.Bll.Events;

/// <summary>
/// Порт публікації подій Каталогу (реалізація — Catalog.Api/IntegrationEvents через RabbitMQ).
/// Викликається ЛИШЕ після успішного SaveChanges: якщо запис у БД не вдався, подія не йде.
/// </summary>
public interface IProductEventsPublisher
{
    Task ProductChangedAsync(ProductDto product, decimal? oldPrice, CancellationToken ct);
}

public sealed record StockReservationLine(long ProductId, int Quantity);
