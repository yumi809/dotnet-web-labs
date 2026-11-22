using System.Diagnostics;

namespace Orders.Bll;

/// <summary>Власне джерело spans бізнес-операцій сервісу Замовлень (підключено в ServiceDefaults через BookHub.*).</summary>
public static class OrdersTelemetry
{
    public const string SourceName = "BookHub.Orders";
    public static readonly ActivitySource ActivitySource = new(SourceName, "1.0.0");
}
