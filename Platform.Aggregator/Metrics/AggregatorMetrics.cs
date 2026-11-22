using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Platform.Aggregator.Metrics;

/// <summary>Бізнес-метрики Aggregator: кількість агрегацій, часткові відповіді, тривалість (низька кардинальність тегів).</summary>
public sealed class AggregatorMetrics
{
    public const string MeterName = "Platform.Aggregator";
    private readonly Counter<long> _aggregations;
    private readonly Histogram<double> _duration;

    public AggregatorMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);
        _aggregations = meter.CreateCounter<long>("aggregator.requests", unit: "{request}", description: "Aggregation requests by scenario and result");
        _duration = meter.CreateHistogram<double>("aggregator.duration", unit: "ms", description: "Aggregation duration");
    }

    /// <param name="scenario">order-details | product-card</param>
    /// <param name="result">complete | partial | failed | cached</param>
    public void Record(string scenario, string result, double elapsedMs)
    {
        var tags = new TagList { { "scenario", scenario }, { "result", result } };
        _aggregations.Add(1, tags);
        _duration.Record(elapsedMs, tags);
    }
}
