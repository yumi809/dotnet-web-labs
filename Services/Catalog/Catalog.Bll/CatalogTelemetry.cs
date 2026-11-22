using System.Diagnostics;

namespace Catalog.Bll;

/// <summary>ActivitySource бізнес-операцій Каталогу (BookHub.* підключено в ServiceDefaults).</summary>
public static class CatalogTelemetry
{
    public const string SourceName = "BookHub.Catalog";
    public static readonly ActivitySource ActivitySource = new(SourceName, "1.0.0");
}
