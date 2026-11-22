using Orders.Api.Grpc;
using Orders.Api.Infrastructure;
using Platform.ServiceDefaults.Grpc;
using Orders.Bll;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Serilog, OpenTelemetry, CorrelationId, service discovery, resilience, health checks
builder.AddServiceDefaults();

// BLL + DAL: Unit of Work і сервіси — Scoped; рядок підключення ConnectionStrings:OrdersDb (з AppHost)
builder.Services.AddOrdersBll();

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
app.MapDefaultEndpoints();
app.Run();
