using Catalog.Api.Grpc;
using Catalog.Api.HealthChecks;
using Catalog.Api.IntegrationEvents;
using Catalog.Bll.Events;
using Platform.EventBus;
using Platform.EventBus.Events;
using Catalog.Api.Infrastructure;
using Catalog.Api.Metrics;
using Platform.ServiceDefaults.Grpc;
using Catalog.Bll;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// ServiceDefaults: Serilog (структуровані логи, enrich, sink Console/Compact JSON, команди EF Core),
// OpenTelemetry, CorrelationId, service discovery, resilience, health checks
builder.AddServiceDefaults();

builder.Services.AddCatalogBll(builder.Configuration); // ConnectionStrings:CatalogDb з AppHost

// L2-кеш: IDistributedCache поверх Redis через Aspire-інтеграцію (ConnectionStrings:redis)
builder.AddRedisDistributedCache("redis");

// RabbitMQ (Aspire.RabbitMQ.Client, ресурс "messaging"): публікація ProductUpdated/PriceChanged після SaveChanges
// і споживання OrderCreated (резервування запасу) з власної черги catalog-api.events
builder.AddEventBus();
builder.Services.AddScoped<IProductEventsPublisher, ProductEventsPublisher>();
builder.AddEventConsumer("catalog-api.events")
    .Subscribe<OrderCreatedIntegrationEvent, OrderCreatedHandler>("orders.order.created");

// Бізнес-метрики (Meter "Catalog.Api") + знімок стану для gauge
builder.Services.AddSingleton<CatalogSnapshot>();
builder.Services.AddSingleton<CatalogMetrics>();
builder.Services.AddSingleton<ICatalogMetrics>(sp => sp.GetRequiredService<CatalogMetrics>());
builder.Services.AddHostedService<CatalogSnapshotRefresher>();

// Health checks: catalog-db (Unhealthy), redis-cache (Degraded), gRPC Health Checking Protocol
builder.Services.AddCatalogHealthChecks(builder.Configuration);

// gRPC (HTTP/2) з серверним interceptor-ом логування
builder.Services.AddGrpc(o => o.Interceptors.Add<ServerLoggingInterceptor>());

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o => o.SwaggerDoc("v1", new() { Title = "Catalog API", Version = "v1" }));

var app = builder.Build();

app.UseServiceDefaults();
app.UseSerilogRequestLogging(o =>
    o.EnrichDiagnosticContext = (diag, http) => diag.Set("ClientIp", http.Connection.RemoteIpAddress?.ToString()));
app.UseExceptionHandler();
app.UseStatusCodePages();

// MigrateAsync + програмний сідінг; сідінг вимикається в production через Catalog:SeedData=false
await app.Services.InitializeCatalogDatabaseAsync(app.Configuration.GetValue("Catalog:SeedData", app.Environment.IsDevelopment()));

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();
app.MapGrpcService<CatalogGrpcService>();
app.MapGrpcHealthChecksService();
app.MapDefaultEndpoints();
app.Run();
