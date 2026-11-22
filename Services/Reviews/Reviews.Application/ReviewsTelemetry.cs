using System.Diagnostics;

namespace Reviews.Application;

/// <summary>ActivitySource бізнес-операцій сервісу Відгуків (BookHub.* підключено в ServiceDefaults).</summary>
public static class ReviewsTelemetry
{
    public const string SourceName = "BookHub.Reviews";
    public static readonly ActivitySource ActivitySource = new(SourceName, "1.0.0");
}
