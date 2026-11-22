using System.Diagnostics.Metrics;

namespace Catalog.Bll.Caching;

/// <summary>
/// Метрики кешу через Meter API: лічильники cache.hits / cache.misses з тегом рівня (L1 / L2).
/// Meter "Catalog.Cache" підключено в ServiceDefaults (AddMeter). Singleton.
/// </summary>
public sealed class CacheMetrics
{
    public const string MeterName = "Catalog.Cache";

    private readonly Counter<long> _hits;
    private readonly Counter<long> _misses;
    private readonly Counter<long> _invalidations;

    public CacheMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);
        _hits = meter.CreateCounter<long>("cache.hits", unit: "{hit}", description: "Cache hits by level");
        _misses = meter.CreateCounter<long>("cache.misses", unit: "{miss}", description: "Cache misses by level");
        _invalidations = meter.CreateCounter<long>("cache.invalidations", unit: "{key}", description: "Invalidated cache keys");
    }

    public void Hit(string level, string entity) =>
        _hits.Add(1, new KeyValuePair<string, object?>("cache.level", level), new KeyValuePair<string, object?>("cache.entity", entity));

    public void Miss(string level, string entity) =>
        _misses.Add(1, new KeyValuePair<string, object?>("cache.level", level), new KeyValuePair<string, object?>("cache.entity", entity));

    public void Invalidated(string reason) =>
        _invalidations.Add(1, new KeyValuePair<string, object?>("reason", reason));
}
