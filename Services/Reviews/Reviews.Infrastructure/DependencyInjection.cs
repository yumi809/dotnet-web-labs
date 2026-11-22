using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using MongoDB.Driver.Core.Extensions.DiagnosticSources;
using Reviews.Application.Interfaces;
using Reviews.Domain.Interfaces;
using Reviews.Infrastructure.Persistence;
using Reviews.Infrastructure.Queries;
using Reviews.Infrastructure.Repositories;

namespace Reviews.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MongoDbSettings>(configuration.GetSection(MongoDbSettings.SectionName));
        services.PostConfigure<MongoDbSettings>(settings =>
        {
            // Під Aspire рядок підключення приходить як ConnectionStrings:ReviewsDb (WithReference у AppHost)
            var fromAspire = configuration.GetConnectionString(MongoDbSettings.ConnectionStringName);
            if (!string.IsNullOrWhiteSpace(fromAspire))
            {
                settings.ConnectionString = fromAspire;
                settings.DatabaseName = MongoUrl.Create(fromAspire).DatabaseName ?? settings.DatabaseName;
            }
        });
        services.AddSingleton<IMongoClient>(sp =>
        {
            var mongoSettings = MongoClientSettings.FromConnectionString(sp.GetRequiredService<IOptions<MongoDbSettings>>().Value.ConnectionString);
            // Трасування команд MongoDB в OpenTelemetry (ActivitySource MongoDB.Driver.Core.Extensions.DiagnosticSources)
            mongoSettings.ClusterConfigurator = cb => cb.Subscribe(new DiagnosticsActivityEventSubscriber(new InstrumentationOptions { CaptureCommandText = true }));
            mongoSettings.ServerSelectionTimeout = TimeSpan.FromSeconds(5);
            return new MongoClient(mongoSettings);
        });

        services.AddSingleton<MongoDbContext>();
        services.AddScoped<IReviewRepository, ReviewRepository>();
        services.AddScoped<IDiscussionRepository, DiscussionRepository>();
        services.AddScoped<IReviewQueries, ReviewQueries>();
        services.AddScoped<Reviews.Application.Commands.IProcessedEventStore, ProcessedEventStore>();
        services.AddSingleton<IDataSeeder, ReviewsDataSeeder>();
        services.AddHostedService<MongoIndexInitializer>();
        return services;
    }
}
