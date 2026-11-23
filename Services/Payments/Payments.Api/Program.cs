using Payments.Api;
using Platform.ServiceDefaults.Authorization;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

// Демо-сервіс для перевірки доступу (Лаб. 09): resource server payments-api у Keycloak.
// За матрицею ролей платежі доступні ЛИШЕ admin (permissions payments:*).
builder.AddPlatformAuthentication(audience: "payments-api")
    .AddKeycloakJwtBearer(
        serviceName: "keycloak",
        realm: PlatformAuthentication.KeycloakRealm,
        authenticationScheme: PlatformAuthentication.KeycloakScheme,
        configureOptions: o =>
        {
            o.Audience = "payments-api";
            o.RequireHttpsMetadata = false;
            o.MapInboundClaims = false;
        });

builder.Services.AddSingleton<PaymentStore>();
builder.Services.AddControllers();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseServiceDefaults();
app.UseSerilogRequestLogging();
app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapDefaultEndpoints();
app.Run();
