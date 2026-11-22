using System.Text.Json;
using System.Text.Json.Serialization;
using Platform.Aggregator.Clients;
using Platform.Aggregator.Services;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults(); // resilience + service discovery + CorrelationId для всіх HttpClient

// Typed HttpClients: логічні імена ресурсів Aspire, схема https+http — спершу https, інакше http
builder.Services.AddHttpClient<OrdersClient>(c => c.BaseAddress = new Uri("https+http://orders-api"));
builder.Services.AddHttpClient<CatalogClient>(c => c.BaseAddress = new Uri("https+http://catalog-api"));
builder.Services.AddHttpClient<ReviewsClient>(c => c.BaseAddress = new Uri("https+http://reviews-api"));
builder.Services.AddScoped<AggregationService>();

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

api.MapGet("/orders/{id:long}/details", async (long id, AggregationService service, CancellationToken ct) =>
{
    try
    {
        var details = await service.GetOrderDetailsAsync(id, ct);
        return details is null ? Results.Problem(statusCode: 404, title: $"Order {id} not found") : Results.Ok(details);
    }
    catch (DownstreamUnavailableException ex)
    {
        return Results.Problem(statusCode: 503, title: "Required service is unavailable", detail: ex.Message);
    }
}).WithName("GetOrderDetails");

api.MapGet("/products/{id:long}", async (long id, AggregationService service, CancellationToken ct) =>
{
    try
    {
        var card = await service.GetProductCardAsync(id, ct);
        return card is null ? Results.Problem(statusCode: 404, title: $"Product {id} not found") : Results.Ok(card);
    }
    catch (DownstreamUnavailableException ex)
    {
        return Results.Problem(statusCode: 503, title: "Required service is unavailable", detail: ex.Message);
    }
}).WithName("GetProductCard");

app.MapDefaultEndpoints();
app.Run();
