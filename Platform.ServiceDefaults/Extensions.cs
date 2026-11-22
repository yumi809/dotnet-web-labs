using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Platform.ServiceDefaults.Correlation;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Спільні налаштування всіх сервісів платформи: Serilog, OpenTelemetry, CorrelationId,
/// service discovery, resilience для HttpClient і health checks.
/// </summary>
public static class Extensions
{
    /// <summary>Префікс ActivitySource/Meter усіх бізнес-компонентів платформи.</summary>
    public const string TelemetryPrefix = "BookHub";

    /// <summary>
    /// Meter-и, що експортуються в Aspire Dashboard: бізнес-метрики сервісів (Orders.Api, Catalog.Api, Reviews.Api),
    /// метрики кешу (Catalog.Cache), Aggregator і телеметрія resilience-подій Polly.
    /// </summary>
    public static readonly string[] PlatformMeters =
        ["Orders.Api", "Catalog.Api", "Reviews.Api", "Catalog.Cache", "Platform.Aggregator", "Polly", $"{TelemetryPrefix}.*"];

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureSerilog();
        builder.ConfigureOpenTelemetry();
        builder.AddDefaultHealthChecks();

        builder.Services.AddCorrelationId();
        builder.Services.AddServiceDiscovery();
        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            // Standard Resilience Handler (Polly) для ВСІХ HttpClient, у т.ч. gRPC-клієнтів, що створює та сама фабрика:
            // rate limiter → total timeout → retry → circuit breaker → attempt timeout.
            // Клієнти з власними вимогами спершу викликають RemoveAllResilienceHandlers(), щоб обробники не нашаровувались.
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();                              // https+http://orders-api → реальна адреса
            http.AddHttpMessageHandler<CorrelationIdDelegatingHandler>(); // X-Correlation-Id у вихідних запитах
        });
        return builder;
    }

    /// <summary>
    /// Serilog: збагачення ServiceName / Environment / MachineName / ThreadId, LogContext,
    /// рівні за середовищем, CompactJsonFormatter у консоль і передача в інші провайдери (OpenTelemetry → Dashboard).
    /// </summary>
    public static TBuilder ConfigureSerilog<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        var isDevelopment = builder.Environment.IsDevelopment();
        builder.Services.AddSerilog((services, logger) => logger
                .ReadFrom.Configuration(builder.Configuration)
                .ReadFrom.Services(services)
                .MinimumLevel.Is(isDevelopment ? LogEventLevel.Debug : LogEventLevel.Information)
                .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.EntityFrameworkCore", isDevelopment ? LogEventLevel.Information : LogEventLevel.Warning)
                .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("ServiceName", builder.Environment.ApplicationName)
                .Enrich.WithEnvironmentName()
                .Enrich.WithMachineName()
                .Enrich.WithThreadId()
                .WriteTo.Console(new CompactJsonFormatter()),
            writeToProviders: true); // логи також ідуть в OpenTelemetry logging provider (OTLP → Aspire Dashboard)
        return builder;
    }

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter(PlatformMeters))
            .WithTracing(tracing => tracing
                .AddSource(builder.Environment.ApplicationName)
                .AddSource($"{TelemetryPrefix}.*")                                  // власні ActivitySource бізнес-операцій
                .AddSource("BookHub.EventBus")                                      // publish/process через RabbitMQ (traceparent у заголовках)
                .AddSource("MongoDB.Driver.Core.Extensions.DiagnosticSources")     // команди MongoDB
                .AddAspNetCoreInstrumentation(o => o.Filter = ctx =>
                    !ctx.Request.Path.StartsWithSegments(HealthEndpointPath) && !ctx.Request.Path.StartsWithSegments(AlivenessEndpointPath))
                .AddHttpClientInstrumentation()
                .AddSqlClientInstrumentation(o => o.RecordException = true));

        // Експорт OTLP у Aspire Dashboard (AppHost передає OTEL_EXPORTER_OTLP_ENDPOINT)
        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }
        return builder;
    }

    public const string HealthEndpointPath = "/health";
    public const string AlivenessEndpointPath = "/alive";

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);
        return builder;
    }

    /// <summary>
    /// CorrelationId-middleware ставиться першим у конвеєрі (до логування запитів і обробки помилок),
    /// тому викликається на початку Program.cs кожного сервісу.
    /// </summary>
    public static WebApplication UseServiceDefaults(this WebApplication app)
    {
        app.UseCorrelationId();
        return app;
    }

    public const string LivenessEndpointPath = "/health/live";
    public const string ReadinessEndpointPath = "/health/ready";

    /// <summary>
    /// Health endpoints платформи — зареєстровані в УСІХ середовищах (їх використовують проби Kubernetes, Лаб. 10):
    /// <list type="bullet">
    /// <item>/health/live — лише перевірки з тегом "live" (стан процесу, без зовнішніх залежностей, &lt; 100 мс);</item>
    /// <item>/health/ready — перевірки з тегом "ready" (БД, кеш, gRPC-залежності) з JSON-деталями кожної перевірки;</item>
    /// <item>/health і /alive — сумісність із шаблоном Aspire.</item>
    /// </list>
    /// </summary>
    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        app.MapHealthChecks(LivenessEndpointPath, new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("live"),
            ResponseWriter = HealthResponseWriter.WriteJsonAsync
        });
        app.MapHealthChecks(ReadinessEndpointPath, new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("ready"),
            ResponseWriter = HealthResponseWriter.WriteJsonAsync
        });
        app.MapHealthChecks(HealthEndpointPath, new HealthCheckOptions { ResponseWriter = HealthResponseWriter.WriteJsonAsync });
        app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions { Predicate = r => r.Tags.Contains("live") });
        return app;
    }
}
