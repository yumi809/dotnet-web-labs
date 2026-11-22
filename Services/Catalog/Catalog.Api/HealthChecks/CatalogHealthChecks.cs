using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Catalog.Api.HealthChecks;

public static class CatalogHealthChecks
{
    /// <summary>
    /// catalog-db (SQL Server, SELECT 1, Unhealthy) і redis-cache (PING, Degraded — кеш необов'язковий:
    /// сервіс працює без нього, читаючи з БД). Обидві з тегом ready; gRPC Health Checking Protocol.
    /// </summary>
    public static IServiceCollection AddCatalogHealthChecks(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHealthChecks()
            .AddSqlServer(
                connectionString: configuration.GetConnectionString("CatalogDb")!,
                healthQuery: "SELECT 1;",
                name: "catalog-db",
                failureStatus: HealthStatus.Unhealthy,
                tags: ["database", "sql", "ready"],
                timeout: TimeSpan.FromSeconds(5))
            .AddRedis(
                redisConnectionString: configuration.GetConnectionString("redis")!,
                name: "redis-cache",
                failureStatus: HealthStatus.Degraded,        // кеш необов'язковий
                tags: ["cache", "redis", "ready"],
                timeout: TimeSpan.FromSeconds(3));

        services.AddGrpcHealthChecks();
        return services;
    }
}
