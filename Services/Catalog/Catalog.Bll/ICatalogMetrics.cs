namespace Catalog.Bll;

/// <summary>Абстракція бізнес-метрик Каталогу для BLL; реалізація — Catalog.Api/Metrics/CatalogMetrics.</summary>
public interface ICatalogMetrics
{
    void ProductCreated(string category);
    void PriceChanged(decimal oldPrice, decimal newPrice);
    void StockMoved(string operation, int quantity);
    void RecordDuration(string operation, bool success, double elapsedMs);
}
