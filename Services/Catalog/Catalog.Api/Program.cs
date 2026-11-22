using Catalog.Api.Grpc;
using Catalog.Api.Infrastructure;
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
app.MapDefaultEndpoints();
app.Run();
