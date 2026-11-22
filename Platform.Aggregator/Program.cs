using System.Text.Json;
using System.Text.Json.Serialization;
using Catalog.Grpc.V1;
using Orders.Grpc.V1;
using Platform.Aggregator.Clients;
using Platform.Aggregator.Services;
using Reviews.Grpc.V1;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults(); // resilience + service discovery + CorrelationId для всіх HttpClient (у т.ч. gRPC)

// Redis для кешування агрегованих відповідей (Aspire-інтеграція)
builder.AddRedisDistributedCache("redis");

// Typed HttpClients (REST): логічні імена ресурсів Aspire, схема https+http — спершу https, інакше http
builder.Services.AddHttpClient<OrdersClient>(c => c.BaseAddress = new Uri("https+http://orders-api"));
builder.Services.AddHttpClient<CatalogClient>(c => c.BaseAddress = new Uri("https+http://catalog-api"));
builder.Services.AddHttpClient<ReviewsClient>(c => c.BaseAddress = new Uri("https+http://reviews-api"));

// Типізовані gRPC-клієнти для кожного мікросервісу; адреса — логічне ім'я, яке резолвить service discovery
builder.Services.AddGrpcClient<OrdersService.OrdersServiceClient>(o => o.Address = new Uri("https://orders-api"));
builder.Services.AddGrpcClient<CatalogService.CatalogServiceClient>(o => o.Address = new Uri("https://catalog-api"));
builder.Services.AddGrpcClient<ReviewsService.ReviewsServiceClient>(o => o.Address = new Uri("https://reviews-api"));

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
