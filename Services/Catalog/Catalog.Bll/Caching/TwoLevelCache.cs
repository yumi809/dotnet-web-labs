using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Catalog.Bll.Caching;

public interface ICatalogCache
{
    /// <summary>Cache-aside: L1 (пам'ять) → L2 (Redis) → БД; рівні заповнюються на зворотному шляху.</summary>
    Task<T?> GetOrCreateAsync<T>(string key, string entity, CacheTtl ttl, Func<CancellationToken, Task<T?>> factory, CancellationToken ct) where T : class;
    Task RemoveAsync(string key, string reason, CancellationToken ct);
    Task<long> GetListVersionAsync(CancellationToken ct);
    Task BumpListVersionAsync(string reason, CancellationToken ct);
}

/// <summary>
/// Ручна реалізація дворівневого кешу: L1 — IMemoryCache (SizeLimit, Size, sliding + absolute),
/// L2 — IDistributedCache поверх Redis (System.Text.Json, префікс ключів, TTL за типом даних).
/// <para>
/// Graceful degradation (Лаб. 07): кеш — необов'язкова залежність. Кожна операція з Redis у try/catch:
/// збій логується як Warning і НЕ провалює запит — сервіс читає з бази даних.
/// </para>
/// </summary>
public sealed class TwoLevelCache(
    IMemoryCache memory,
    IDistributedCache redis,
    CacheMetrics metrics,
    ILogger<TwoLevelCache> logger) : ICatalogCache
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<T?> GetOrCreateAsync<T>(string key, string entity, CacheTtl ttl, Func<CancellationToken, Task<T?>> factory, CancellationToken ct)
        where T : class
    {
        // L1
        if (memory.TryGetValue(key, out T? l1Value))
        {
            metrics.Hit("L1", entity);
            logger.LogDebug("Cache hit L1 {CacheKey}", key);
            return l1Value;
        }
        metrics.Miss("L1", entity);

        // L2 (Redis) — при недоступності вважаємо промахом і йдемо в БД
        byte[]? bytes = null;
        try
        {
            bytes = await redis.GetAsync(key, ct);
        }
        catch (Exception ex) when (IsRedisFailure(ex))
        {
            logger.LogWarning(ex, "Redis unavailable, falling back to database for {CacheKey}", key);
        }

        if (bytes is not null)
        {
            var l2Value = JsonSerializer.Deserialize<T>(bytes, Json);
            metrics.Hit("L2", entity);
            logger.LogDebug("Cache hit L2 {CacheKey}", key);
            SetL1(key, l2Value, ttl);          // заповнюємо L1 на зворотному шляху
            return l2Value;
        }
        metrics.Miss("L2", entity);
        logger.LogInformation("Cache miss {CacheKey}, loading from database", key);

        // БД
        var value = await factory(ct);
        if (value is null) return null;

        try
        {
            await redis.SetAsync(key, JsonSerializer.SerializeToUtf8Bytes(value, Json),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl.L2Absolute }, ct);
        }
        catch (Exception ex) when (IsRedisFailure(ex))
        {
            logger.LogWarning(ex, "Redis unavailable, value for {CacheKey} not cached in L2", key);
        }
        SetL1(key, value, ttl);
        return value;
    }

    public async Task RemoveAsync(string key, string reason, CancellationToken ct)
    {
        memory.Remove(key);
        try
        {
            await redis.RemoveAsync(key, ct);
        }
        catch (Exception ex) when (IsRedisFailure(ex))
        {
            logger.LogWarning(ex, "Redis unavailable, L2 key {CacheKey} not invalidated (expires by TTL)", key);
        }
        metrics.Invalidated(reason);
        logger.LogInformation("Cache key {CacheKey} invalidated: {Reason}", key, reason);
    }

    public async Task<long> GetListVersionAsync(CancellationToken ct)
    {
        try
        {
            var raw = await redis.GetStringAsync(CacheKeys.ProductListVersion, ct);
            return long.TryParse(raw, out var v) ? v : 0;
        }
        catch (Exception ex) when (IsRedisFailure(ex))
        {
            logger.LogWarning(ex, "Redis unavailable, list version defaults to 0");
            return 0;
        }
    }

    public async Task BumpListVersionAsync(string reason, CancellationToken ct)
    {
        try
        {
            var next = await GetListVersionAsync(ct) + 1;
            await redis.SetStringAsync(CacheKeys.ProductListVersion, next.ToString(), ct);
            metrics.Invalidated(reason);
            logger.LogInformation("Product list cache invalidated (version {Version}): {Reason}", next, reason);
        }
        catch (Exception ex) when (IsRedisFailure(ex))
        {
            logger.LogWarning(ex, "Redis unavailable, product list version not bumped: {Reason}", reason);
        }
    }

    private static bool IsRedisFailure(Exception ex) =>
        ex is RedisConnectionException or RedisTimeoutException or RedisException or TimeoutException or ObjectDisposedException;

    private void SetL1<T>(string key, T value, CacheTtl ttl) =>
        memory.Set(key, value, new MemoryCacheEntryOptions
        {
            Size = 1,                                        // обов'язково при SizeLimit
            AbsoluteExpirationRelativeToNow = ttl.L1Absolute,
            SlidingExpiration = ttl.L1Sliding,
            Priority = CacheItemPriority.Normal
        });
}
