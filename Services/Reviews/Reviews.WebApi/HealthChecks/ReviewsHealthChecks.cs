using Microsoft.Extensions.Diagnostics.HealthChecks;
using MongoDB.Driver;

namespace Reviews.WebApi.HealthChecks;

public static class ReviewsHealthChecks
{
    /// <summary>reviews-db: MongoDB ping через зареєстрований IMongoClient, теги database/mongodb/ready, таймаут 5 с.</summary>
    public static IServiceCollection AddReviewsHealthChecks(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddMongoDb(
                clientFactory: sp => sp.GetRequiredService<IMongoClient>(),
                name: "reviews-db",
                failureStatus: HealthStatus.Unhealthy,
                tags: ["database", "mongodb", "ready"],
                timeout: TimeSpan.FromSeconds(5));

        services.AddGrpcHealthChecks();
        return services;
    }
}
