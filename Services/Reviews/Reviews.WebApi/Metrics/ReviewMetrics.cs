using System.Diagnostics.Metrics;
using Reviews.Application;

namespace Reviews.WebApi.Metrics;

/// <summary>
/// Бізнес-метрики Відгуків (Meter "Reviews.Api", Singleton). Теги: тип відгуку (3 значення) і оцінка (1..5) —
/// низька кардинальність, без Id користувачів/товарів.
/// </summary>
public sealed class ReviewMetrics : IReviewMetrics
{
    public const string MeterName = "Reviews.Api";

    private readonly Counter<long> _reviewsCreated;
    private readonly Histogram<double> _duration;
    private long _ratingSum;
    private long _ratingCount;

    public ReviewMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);
        _reviewsCreated = meter.CreateCounter<long>("reviews.created", unit: "{review}", description: "Created reviews by type and rating");
        _duration = meter.CreateHistogram<double>("reviews.operation.duration", unit: "ms", description: "Duration of review commands");
        meter.CreateObservableGauge("reviews.average_rating", () =>
            Interlocked.Read(ref _ratingCount) == 0 ? 0 : (double)Interlocked.Read(ref _ratingSum) / Interlocked.Read(ref _ratingCount),
            unit: "{star}", description: "Average rating of reviews created since service start");
    }

    public void ReviewCreated(string type, int rating)
    {
        _reviewsCreated.Add(1, new KeyValuePair<string, object?>("review.type", type), new KeyValuePair<string, object?>("review.rating", rating));
        Interlocked.Add(ref _ratingSum, rating);
        Interlocked.Increment(ref _ratingCount);
    }

    public void RecordDuration(string operation, bool success, double elapsedMs) =>
        _duration.Record(elapsedMs,
            new KeyValuePair<string, object?>("operation", operation),
            new KeyValuePair<string, object?>("status", success ? "success" : "failure"));
}
