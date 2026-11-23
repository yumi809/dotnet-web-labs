using System.Diagnostics;
using Platform.ServiceDefaults.Authorization;
using Serilog;
using Yarp.ReverseProxy.Model;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// YARP: маршрути й кластери з appsettings.json; адреси http://<ім'я-ресурсу> резолвить service discovery
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddServiceDiscoveryDestinationResolver();

// Лаб. 09: Gateway валідує JWT (Auth Service / IdentityServer / Keycloak) ще до проксіювання.
// Політика маршруту задається в ReverseProxy:Routes:*:AuthorizationPolicy ("authenticated" / "anonymous").
builder.AddPlatformAuthentication(audience: null)
    .AddKeycloakJwtBearer(
        serviceName: "keycloak",
        realm: PlatformAuthentication.KeycloakRealm,
        authenticationScheme: PlatformAuthentication.KeycloakScheme,
        configureOptions: o =>
        {
            o.RequireHttpsMetadata = false;
            o.MapInboundClaims = false;
            o.TokenValidationParameters.ValidateAudience = false;
        });
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("authenticated", p => p.RequireAuthenticatedUser());

var app = builder.Build();

// CorrelationId-middleware стоїть ПЕРЕД MapReverseProxy: заголовок X-Correlation-Id
// генерується/зберігається тут і передається далі в сервіси разом з проксійованим запитом
app.UseServiceDefaults();
app.UseSerilogRequestLogging();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => Results.Ok(new
{
    service = "BookHub API Gateway",
    routes = new[] { "/auth", "/users", "/api/orders", "/api/catalog", "/api/reviews", "/api/payments", "/api/aggregator" }
}));

app.MapReverseProxy(proxy =>
{
    // Логування маршрутизації: метод, шлях, маршрут, кластер, статус і тривалість
    proxy.Use(async (context, next) =>
    {
        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Gateway.Routing");
        var proxyFeature = context.GetReverseProxyFeature();
        var sw = Stopwatch.StartNew();
        await next();
        sw.Stop();
        logger.LogInformation(
            "Proxied {Method} {Path} via route {RouteId} to cluster {ClusterId} -> {StatusCode} in {ElapsedMs} ms",
            context.Request.Method, context.Request.Path, proxyFeature.Route.Config.RouteId,
            proxyFeature.Cluster?.Config.ClusterId, context.Response.StatusCode, sw.ElapsedMilliseconds);
    });
});

app.MapDefaultEndpoints();
app.Run();
