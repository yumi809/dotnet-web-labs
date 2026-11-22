using Catalog.Api.Infrastructure;
using Catalog.Bll;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Serilog: структуровані логи, конфігурація з appsettings (sink Console, enrich FromLogContext/MachineName),
// команди EF Core логуються категорією Microsoft.EntityFrameworkCore.Database.Command
builder.Host.UseSerilog((context, services, logger) => logger
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Service", "catalog-api"));

builder.Services.AddCatalogBll(builder.Configuration);
builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o => o.SwaggerDoc("v1", new() { Title = "Catalog API", Version = "v1" }));

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseSerilogRequestLogging(o =>
    o.EnrichDiagnosticContext = (diag, http) => diag.Set("ClientIp", http.Connection.RemoteIpAddress?.ToString()));

// MigrateAsync + програмний сідінг; сідінг вимикається в production через Catalog:SeedData=false
await app.Services.InitializeCatalogDatabaseAsync(app.Configuration.GetValue("Catalog:SeedData", app.Environment.IsDevelopment()));

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();
app.Run();
