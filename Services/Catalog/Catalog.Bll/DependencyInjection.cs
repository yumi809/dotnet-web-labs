using Catalog.Bll.Caching;
using Catalog.Bll.Mapping;
using Catalog.Bll.Services;
using Catalog.Dal;
using Catalog.Dal.Seeding;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Catalog.Bll;

public static class DependencyInjection
{
    public static IServiceCollection AddCatalogBll(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddCatalogDal(configuration);
        services.AddAutoMapper(cfg => { }, typeof(CatalogMappingProfile).Assembly);
        // Валідатори FluentValidation — окремі класи, викликаються явно в сервісах (без застарілого AddFluentValidation)
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);
        // L1: IMemoryCache з обмеженням розміру (кожен запис має Size = 1); L2 (IDistributedCache) реєструє
        // Aspire-інтеграція AddRedisDistributedCache("redis") у Program.cs
        services.AddMemoryCache(o =>
        {
            o.SizeLimit = 1_000;
            o.CompactionPercentage = 0.2;           // при переповненні витісняється 20% найменш цінних записів
            o.ExpirationScanFrequency = TimeSpan.FromMinutes(1);
        });
        services.AddSingleton<CacheMetrics>();
        services.AddScoped<ICatalogCache, TwoLevelCache>();
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<IStockReservationService, StockReservationService>();
        services.AddScoped<IPublisherService, PublisherService>();
        services.AddScoped<ICategoryService, CategoryService>();
        return services;
    }

    /// <summary>MigrateAsync + ідемпотентний сідінг (керується Catalog:SeedData).</summary>
    public static async Task InitializeCatalogDatabaseAsync(this IServiceProvider services, bool seedData, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DbInitializer>().InitializeAsync(seedData, ct);
    }
}
