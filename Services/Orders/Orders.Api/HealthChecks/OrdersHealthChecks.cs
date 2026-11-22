using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Orders.Api.HealthChecks;

public static class OrdersHealthChecks
{
    /// <summary>
    /// SQL Server (OrdersDb): легкий SELECT 1, ім'я orders-db, теги database/sql/ready, таймаут 5 с;
    /// gRPC Health Checking Protocol відповідає на основі тих самих перевірок.
    /// </summary>
    public static IServiceCollection AddOrdersHealthChecks(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHealthChecks()
            .AddSqlServer(
                connectionString: configuration.GetConnectionString("OrdersDb")!,
                healthQuery: "SELECT 1;",
                name: "orders-db",
                failureStatus: HealthStatus.Unhealthy,
                tags: ["database", "sql", "ready"],
                timeout: TimeSpan.FromSeconds(5));

        services.AddGrpcHealthChecks();
        return services;
    }
}
