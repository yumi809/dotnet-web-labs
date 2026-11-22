using Orders.Api.Grpc;
using Orders.Api.HealthChecks;
using Orders.Api.Infrastructure;
using Orders.Api.Metrics;
using Orders.Bll;
using Platform.ServiceDefaults.Grpc;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Serilog, OpenTelemetry, CorrelationId, service discovery, resilience, health checks
builder.AddServiceDefaults();

// BLL + DAL: Unit of Work і сервіси — Scoped; рядок підключення ConnectionStrings:OrdersDb (з AppHost)
builder.Services.AddOrdersBll();

// Бізнес-метрики (Singleton, Meter "Orders.Api") + знімок стану для gauge
builder.Services.AddSingleton<OrdersSnapshot>();
builder.Services.AddSingleton<OrderMetrics>();
builder.Services.AddSingleton<IOrderMetrics>(sp => sp.GetRequiredService<OrderMetrics>());
builder.Services.AddHostedService<OrdersSnapshotRefresher>();

// Health checks: orders-db (ready) + gRPC Health Checking Protocol
builder.Services.AddOrdersHealthChecks(builder.Configuration);

builder.Services.AddGrpc(o => o.Interceptors.Add<ServerLoggingInterceptor>());
builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o => o.SwaggerDoc("v1", new() { Title = "Orders API", Version = "v1" }));

var app = builder.Build();

app.UseServiceDefaults();          // X-Correlation-Id → LogContext
app.UseSerilogRequestLogging();
app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    // Розгортання OrdersDb скриптами db/p1 (ідемпотентні)
    await app.Services.InitializeOrdersDatabaseAsync();

    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();
app.MapGrpcService<OrdersGrpcService>();
app.MapGrpcHealthChecksService();
app.MapDefaultEndpoints();         // /health/live, /health/ready — у всіх середовищах
app.Run();
