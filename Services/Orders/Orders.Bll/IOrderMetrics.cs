namespace Orders.Bll;

/// <summary>
/// Абстракція бізнес-метрик для BLL (реалізація — Orders.Api/Metrics/OrderMetrics на Meter API),
/// щоб зберегти напрямок залежностей Api → Bll.
/// </summary>
public interface IOrderMetrics
{
    void OrderCreated(string status);
    void StatusChanged(string fromStatus, string toStatus);
    void RecordDuration(string operation, bool success, double elapsedMs);
}
