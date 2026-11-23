using System.Text.Json;
using System.Text.Json.Serialization;
using Catalog.Grpc.V1;
using Orders.Grpc.V1;
using Grpc.Health.V1;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Platform.Aggregator.Auth;
using Platform.Aggregator.Clients;
using Platform.Aggregator.HealthChecks;
using Platform.Aggregator.Metrics;
using Platform.Aggregator.Resilience;
using Platform.Aggregator.Services;
using Reviews.Grpc.V1;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults(); // resilience + service discovery + CorrelationId для всіх HttpClient (у т.ч. gRPC)

// Redis для кешування агрегованих відповідей (Aspire-інтеграція)
builder.AddRedisDistributedCache("redis");

// Typed HttpClients (REST): логічні імена ресурсів Aspire, схема https+http — спершу https, інакше http.
// Orders — стандартний обробник із ServiceDefaults; Catalog/Reviews — власні політики за критичністю (Лаб. 07).
// Лаб. 09: Aggregator — клієнт IdentityServer з Client Credentials; токен додається до всіх викликів сервісів
builder.Services.Configure<AggregatorClientOptions>(builder.Configuration.GetSection(AggregatorClientOptions.SectionName));
builder.Services.AddHttpClient("identityserver", c => c.BaseAddress = new Uri("https+http://identityserver"));
builder.Services.AddSingleton<ClientCredentialsTokenProvider>();
builder.Services.AddTransient<ClientCredentialsTokenHandler>();

builder.Services.AddHttpClient<OrdersClient>(c => c.BaseAddress = new Uri("https+http://orders-api"))
    .AddHttpMessageHandler<ClientCredentialsTokenHandler>();
builder.Services.AddHttpClient<CatalogClient>(c => c.BaseAddress = new Uri("https+http://catalog-api"))
    .AddHttpMessageHandler<ClientCredentialsTokenHandler>()
    .AddCriticalResilience("catalog-http");
builder.Services.AddHttpClient<ReviewsClient>(c => c.BaseAddress = new Uri("https+http://reviews-api"))
    .AddHttpMessageHandler<ClientCredentialsTokenHandler>()
    .AddNonCriticalResilience("reviews-http");

// Типізовані gRPC-клієнти для кожного мікросервісу; адреса — логічне ім'я, яке резолвить service discovery.
// Retry за gRPC-статусами — політикою каналу; HTTP-рівень — ті ж політики за критичністю.
// gRPC-сервіси вимагають scope-політики (orders.read / catalog.read / reviews.read) — токен Client Credentials.
builder.Services.AddGrpcClient<OrdersService.OrdersServiceClient>(o => o.Address = new Uri("https://orders-api"))
    .AddHttpMessageHandler<ClientCredentialsTokenHandler>()
    .WithGrpcRetry(maxAttempts: 3, initialBackoff: TimeSpan.FromSeconds(0.5));
builder.Services.AddGrpcClient<CatalogService.CatalogServiceClient>(o => o.Address = new Uri("https://catalog-api"))
    .AddHttpMessageHandler<ClientCredentialsTokenHandler>()
    .WithGrpcRetry(maxAttempts: 4, initialBackoff: TimeSpan.FromSeconds(0.5))
    .AddCriticalResilience("catalog-grpc");
builder.Services.AddGrpcClient<ReviewsService.ReviewsServiceClient>(o => o.Address = new Uri("https://reviews-api"))
    .AddHttpMessageHandler<ClientCredentialsTokenHandler>()
    .WithGrpcRetry(maxAttempts: 2, initialBackoff: TimeSpan.FromSeconds(0.2))
    .AddNonCriticalResilience("reviews-grpc");

// Health checks залежностей: стандартний gRPC Health Checking Protocol кожного сервісу
builder.Services.AddGrpcClient<Health.HealthClient>("orders-health", o => o.Address = new Uri("https://orders-api"));
builder.Services.AddGrpcClient<Health.HealthClient>("catalog-health", o => o.Address = new Uri("https://catalog-api"));
builder.Services.AddGrpcClient<Health.HealthClient>("reviews-health", o => o.Address = new Uri("https://reviews-api"));
builder.Services.AddHealthChecks()
    .AddTypeActivatedCheck<GrpcDependencyHealthCheck>("catalog-api", failureStatus: HealthStatus.Unhealthy, tags: ["grpc", "downstream", "ready"], args: "catalog-health")
    .AddTypeActivatedCheck<GrpcDependencyHealthCheck>("orders-api", failureStatus: HealthStatus.Unhealthy, tags: ["grpc", "downstream", "ready"], args: "orders-health")
    // Відгуки некритичні — їх недоступність робить Aggregator Degraded, а не Unhealthy
    .AddTypeActivatedCheck<GrpcDependencyHealthCheck>("reviews-api", failureStatus: HealthStatus.Degraded, tags: ["grpc", "downstream", "ready"], args: "reviews-health")
    .AddRedis(sp => sp.GetRequiredService<IConfiguration>().GetConnectionString("redis")!, name: "redis-cache",
        failureStatus: HealthStatus.Degraded, tags: ["cache", "redis", "ready"], timeout: TimeSpan.FromSeconds(3));

builder.Services.AddSingleton<AggregatorMetrics>();

builder.Services.AddScoped<AggregationService>();
builder.Services.AddScoped<GrpcAggregationService>();

// camelCase, null-значення не серіалізуються
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseServiceDefaults();
app.UseSerilogRequestLogging();
app.UseExceptionHandler();

var api = app.MapGroup("/api/aggregator");

// ---- v1: REST (Лаб. 05) ----
api.MapGet("/orders/{id:long}/details", async (long id, AggregationService service, CancellationToken ct) =>
    await Respond(() => service.GetOrderDetailsAsync(id, ct), $"Order {id} not found"));

api.MapGet("/products/{id:long}", async (long id, AggregationService service, CancellationToken ct) =>
    await Respond(() => service.GetProductCardAsync(id, ct), $"Product {id} not found"));

// ---- v2: gRPC + кеш агрегованої відповіді в Redis (Лаб. 06) ----
api.MapGet("/v2/orders/{id:long}/details", async (long id, GrpcAggregationService service, CancellationToken ct) =>
    await Respond(() => service.GetOrderDetailsAsync(id, ct), $"Order {id} not found"));

api.MapGet("/v2/products/{id:long}", async (long id, GrpcAggregationService service, CancellationToken ct) =>
    await Respond(() => service.GetProductCardAsync(id, ct), $"Product {id} not found"));

app.MapDefaultEndpoints();
app.Run();

static async Task<IResult> Respond<T>(Func<Task<T?>> action, string notFound) where T : class
{
    try
    {
        return await action() is { } result ? Results.Ok(result) : Results.Problem(statusCode: 404, title: notFound);
    }
    catch (DownstreamUnavailableException ex)
    {
        return Results.Problem(statusCode: 503, title: "Required service is unavailable", detail: ex.Message);
    }
}
