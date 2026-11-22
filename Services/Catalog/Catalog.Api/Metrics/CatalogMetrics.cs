using System.Diagnostics.Metrics;
using Catalog.Bll;
using Catalog.Bll.Services;

namespace Catalog.Api.Metrics;

/// <summary>
/// Бізнес-метрики Каталогу (Meter "Catalog.Api", Singleton). Теги низької кардинальності:
/// категорія (довідник з 5 значень), напрямок зміни ціни, тип операції, результат.
/// </summary>
public sealed class CatalogMetrics : ICatalogMetrics
{
    public const string MeterName = "Catalog.Api";

    private readonly Counter<long> _productsCreated;
    private readonly Counter<long> _priceChanges;
    private readonly Counter<long> _stockUnits;
    private readonly Histogram<double> _operationDuration;

    public CatalogMetrics(IMeterFactory meterFactory, CatalogSnapshot snapshot)
    {
        var meter = meterFactory.Create(MeterName);
        _productsCreated = meter.CreateCounter<long>("products.created", unit: "{product}", description: "Created products");
        _priceChanges = meter.CreateCounter<long>("products.price_changed", unit: "{change}", description: "Price changes by direction");
        _stockUnits = meter.CreateCounter<long>("products.stock_moved", unit: "{unit}", description: "Units added to / removed from stock");
        _operationDuration = meter.CreateHistogram<double>("catalog.operation.duration", unit: "ms", description: "Duration of catalog write operations");
        meter.CreateObservableGauge("products.low_stock", () => snapshot.LowStockCount,
            unit: "{product}", description: "Active products with stock <= 5");
    }

    public void ProductCreated(string category) =>
        _productsCreated.Add(1, new KeyValuePair<string, object?>("product.category", category));

    public void PriceChanged(decimal oldPrice, decimal newPrice)
    {
        if (oldPrice == newPrice) return;
        _priceChanges.Add(1, new KeyValuePair<string, object?>("direction", newPrice > oldPrice ? "up" : "down"));
    }

    public void StockMoved(string operation, int quantity) =>
        _stockUnits.Add(quantity, new KeyValuePair<string, object?>("operation", operation));

    public void RecordDuration(string operation, bool success, double elapsedMs) =>
        _operationDuration.Record(elapsedMs,
            new KeyValuePair<string, object?>("operation", operation),
            new KeyValuePair<string, object?>("status", success ? "success" : "failure"));
}

public sealed class CatalogSnapshot
{
    public long LowStockCount { get; set; }
}

/// <summary>Періодично оновлює знімок для gauge (не на кожен збір метрик).</summary>
public sealed class CatalogSnapshotRefresher(IServiceScopeFactory scopeFactory, CatalogSnapshot snapshot, ILogger<CatalogSnapshotRefresher> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var products = scope.ServiceProvider.GetRequiredService<IProductService>();
                snapshot.LowStockCount = (await products.GetLowStockAsync(5, stoppingToken)).Count;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning("Catalog snapshot refresh failed: {Error}", ex.Message);
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
