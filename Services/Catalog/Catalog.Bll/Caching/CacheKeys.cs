namespace Catalog.Bll.Caching;

/// <summary>Передбачувані ключі кешу з префіксом сервісу.</summary>
public static class CacheKeys
{
    public const string Prefix = "catalog:";

    public static string Product(long id) => $"{Prefix}product:{id}";
    public static string Categories => $"{Prefix}categories:all";
    public static string Publishers => $"{Prefix}publishers:all";
    /// <summary>Версія списків: інкремент інвалідує всі закешовані сторінки списку товарів одразу.</summary>
    public static string ProductListVersion => $"{Prefix}products:list:version";
    public static string ProductList(long version, string queryHash) => $"{Prefix}products:list:v{version}:{queryHash}";
}

/// <summary>TTL за типом даних; L1 (пам'ять) завжди коротший за L2 (Redis).</summary>
public sealed record CacheTtl(TimeSpan L1Absolute, TimeSpan L1Sliding, TimeSpan L2Absolute)
{
    /// <summary>Картка товару: змінюється рідко, читається часто.</summary>
    public static readonly CacheTtl Product = new(TimeSpan.FromMinutes(2), TimeSpan.FromSeconds(45), TimeSpan.FromMinutes(10));
    /// <summary>Сторінки списку: залежать від багатьох товарів — коротший TTL.</summary>
    public static readonly CacheTtl ProductList = new(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(15), TimeSpan.FromMinutes(2));
    /// <summary>Довідники (категорії, видавництва): майже статичні.</summary>
    public static readonly CacheTtl Reference = new(TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(5), TimeSpan.FromHours(1));
}
