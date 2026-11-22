using System.Diagnostics.Metrics;
using Orders.Bll;

namespace Orders.Api.Metrics;

/// <summary>
/// Бізнес-метрики сервісу Замовлень (OpenTelemetry Meter API). Singleton; Meter "Orders.Api" додано в ServiceDefaults.
/// Теги низької кардинальності: статус, операція, результат — жодних Id користувачів/замовлень.
/// </summary>
public sealed class OrderMetrics : IOrderMetrics
{
    public const string MeterName = "Orders.Api";

    private readonly Counter<long> _ordersCreated;
    private readonly Counter<long> _statusChanges;
    private readonly Histogram<double> _operationDuration;

    public OrderMetrics(IMeterFactory meterFactory, OrdersSnapshot snapshot)
    {
        var meter = meterFactory.Create(MeterName);
        _ordersCreated = meter.CreateCounter<long>(
            "orders.created", unit: "{order}", description: "Number of created orders");
        _statusChanges = meter.CreateCounter<long>(
            "orders.status_changed", unit: "{transition}", description: "Order status transitions");
        _operationDuration = meter.CreateHistogram<double>(
            "orders.operation.duration", unit: "ms", description: "Duration of order operations");

        // Gauge — знімок поточного стану, значення читається callback-ом у момент збору метрик
        meter.CreateObservableGauge("orders.awaiting_confirmation", () => snapshot.AwaitingConfirmation,
            unit: "{order}", description: "Orders in status New waiting for confirmation");
        meter.CreateObservableGauge("orders.awaiting_shipment", () => snapshot.AwaitingShipment,
            unit: "{order}", description: "Paid orders waiting for shipment");
    }

    public void OrderCreated(string status) =>
        _ordersCreated.Add(1, new KeyValuePair<string, object?>("order.status", status));

    public void StatusChanged(string fromStatus, string toStatus) =>
        _statusChanges.Add(1,
            new KeyValuePair<string, object?>("order.status.from", fromStatus),
            new KeyValuePair<string, object?>("order.status.to", toStatus));

    /// <summary>Записується для ВСІХ виконань, включно з невдалими.</summary>
    public void RecordDuration(string operation, bool success, double elapsedMs) =>
        _operationDuration.Record(elapsedMs,
            new KeyValuePair<string, object?>("operation", operation),
            new KeyValuePair<string, object?>("status", success ? "success" : "failure"));
}
