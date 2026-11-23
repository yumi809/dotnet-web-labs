using Reviews.Application;
using Reviews.Infrastructure;
using Platform.EventBus;
using Platform.EventBus.Events;
using Platform.ServiceDefaults.Authorization;
using Platform.ServiceDefaults.Grpc;
using Reviews.WebApi.IntegrationEvents;
using Reviews.WebApi.Grpc;
using Reviews.WebApi.HealthChecks;
using Reviews.WebApi.Metrics;
using Reviews.WebApi.Infrastructure;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();                                     // Serilog, OpenTelemetry, CorrelationId, discovery, resilience
builder.Services.AddApplication();                                // MediatR + behaviors + валідатори + AutoMapper
builder.Services.AddInfrastructure(builder.Configuration);        // MongoDB (ConnectionStrings:ReviewsDb / секція MongoDb)
builder.Services.AddAutoMapper(cfg => { }, typeof(Program).Assembly); // request → command

// Лаб. 09: читання відгуків — публічні, запис — лише автентифікованим (автор з claims токена)
builder.AddPlatformAuthentication(audience: "reviews-api")
    .AddKeycloakJwtBearer(
        serviceName: "keycloak",
        realm: PlatformAuthentication.KeycloakRealm,
        authenticationScheme: PlatformAuthentication.KeycloakScheme,
        configureOptions: o =>
        {
            o.Audience = "reviews-api";
            o.RequireHttpsMetadata = false;
            o.MapInboundClaims = false;
        });

// RabbitMQ: споживання ProductUpdated з черги reviews-api.events (оновлення productName у документах)
builder.AddEventBus();
builder.AddEventConsumer("reviews-api.events")
    .Subscribe<ProductUpdatedIntegrationEvent, ProductUpdatedHandler>("catalog.product.updated");

builder.Services.AddSingleton<ReviewMetrics>();
builder.Services.AddSingleton<IReviewMetrics>(sp => sp.GetRequiredService<ReviewMetrics>());
builder.Services.AddReviewsHealthChecks();               // reviews-db (MongoDB ping) + gRPC health
builder.Services.AddGrpc(o => o.Interceptors.Add<ServerLoggingInterceptor>());
builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o => o.SwaggerDoc("v1", new() { Title = "Reviews API", Version = "v1" }));

var app = builder.Build();

app.UseServiceDefaults();
app.UseSerilogRequestLogging();
app.UseExceptionHandler();
app.UseStatusCodePages();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapGrpcService<ReviewsGrpcService>();
app.MapGrpcHealthChecksService();
app.MapDefaultEndpoints();
app.Run();
