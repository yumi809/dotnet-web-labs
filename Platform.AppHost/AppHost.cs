var builder = DistributedApplication.CreateBuilder(args);

// ---------- Секрети (user-secrets: Parameters:<name> або запит у Dashboard) ----------
var sqlPassword = builder.AddParameter("sql-password", secret: true);
var jwtSigningKey = builder.AddParameter("jwt-signing-key", secret: true);              // ключ JWT Auth Service (≥ 32 символи)
var aggregatorSecret = builder.AddParameter("aggregator-client-secret", secret: true);  // client "aggregator" в IdentityServer
var seedPassword = builder.AddParameter("seed-user-password", secret: true);            // тестові користувачі Auth/IdentityServer
var keycloakAdminPassword = builder.AddParameter("keycloak-admin-password", secret: true);

// ---------- Інфраструктура ----------
var sql = builder.AddSqlServer("sql", sqlPassword)
    .WithDataVolume("bookhub-sql-data")          // постійне сховище між перезапусками
    .WithLifetime(ContainerLifetime.Persistent);
var ordersDb = sql.AddDatabase("OrdersDb");                  // = ConnectionStrings:OrdersDb в Orders.Api
var catalogDb = sql.AddDatabase("CatalogDb");                // = ConnectionStrings:CatalogDb в Catalog.Api
var identityDb = sql.AddDatabase("IdentityDb");              // Auth Service (ASP.NET Identity + refresh-токени)
var identityServerDb = sql.AddDatabase("IdentityServerDb");  // Duende IdentityServer (users + configuration + operational)

var mongo = builder.AddMongoDB("mongo")
    .WithDataVolume("bookhub-mongo-data")
    .WithLifetime(ContainerLifetime.Persistent);
var reviewsDb = mongo.AddDatabase("ReviewsDb");  // = ConnectionStrings:ReviewsDb в Reviews.WebApi

// Redis — L2-кеш Каталогу і кеш агрегованих відповідей Aggregator
var redis = builder.AddRedis("redis")
    .WithDataVolume("bookhub-redis-data")
    .WithLifetime(ContainerLifetime.Persistent);

// RabbitMQ — брокер інтеграційних подій: постійне сховище + Management UI (порт 15672)
var messaging = builder.AddRabbitMQ("messaging")
    .WithDataVolume("bookhub-rabbitmq-data")
    .WithManagementPlugin()
    .WithLifetime(ContainerLifetime.Persistent);

// Keycloak (траєкторія А): контейнер через Aspire з постійним volume; realm bookhub, resource servers,
// ролі, клієнти й користувачі створює Pulumi-проєкт infra/keycloak
var keycloak = builder.AddKeycloak("keycloak", port: 8080, adminPassword: keycloakAdminPassword)
    .WithDataVolume("bookhub-keycloak-data")
    .WithLifetime(ContainerLifetime.Persistent);

// ---------- Identity ----------
var authApi = builder.AddProject<Projects.Auth_Api>("auth-api")
    .WithReference(identityDb)
    .WaitFor(identityDb)
    .WithEnvironment("Jwt__SigningKey", jwtSigningKey)
    .WithEnvironment("Seed__AdminPassword", seedPassword)
    .WithHttpHealthCheck("/health/live");

var identityServer = builder.AddProject<Projects.Platform_IdentityServer>("identityserver")
    .WithReference(identityServerDb)
    .WaitFor(identityServerDb)
    .WithEnvironment("Clients__Aggregator__Secret", aggregatorSecret)
    .WithEnvironment("Seed__UserPassword", seedPassword)
    .WithHttpHealthCheck("/health/live");

// ---------- Мікросервіси (БД → сервіси) ----------
// WithHttpHealthCheck: Dashboard показує стан ресурсу (Healthy / Degraded / Unhealthy),
// а WaitFor у залежних ресурсів чекає, поки сервіс стане ЗДОРОВИМ, а не лише запуститься.
const string ReadinessPath = "/health/ready";

var ordersApi = builder.AddProject<Projects.Orders_Api>("orders-api")
    .WithReference(ordersDb)
    .WaitFor(ordersDb)
    .WithReference(messaging)          // publisher OrderCreated + consumer ProductUpdated
    .WaitFor(messaging)
    .WithReference(identityServer)     // Authority для токенів IdentityServer
    .WithReference(keycloak)           // JWT Bearer на realm bookhub
    .WaitFor(keycloak)
    .WithEnvironment("Jwt__SigningKey", jwtSigningKey)
    .WithHttpHealthCheck(ReadinessPath);

var catalogApi = builder.AddProject<Projects.Catalog_Api>("catalog-api")
    .WithReference(catalogDb)
    .WaitFor(catalogDb)
    .WithReference(redis)
    .WaitFor(redis)
    .WithReference(messaging)          // publisher ProductUpdated + consumer OrderCreated
    .WaitFor(messaging)
    .WithReference(identityServer)
    .WithReference(keycloak)
    .WaitFor(keycloak)
    .WithEnvironment("Jwt__SigningKey", jwtSigningKey)
    .WithHttpHealthCheck(ReadinessPath);

var reviewsApi = builder.AddProject<Projects.Reviews_WebApi>("reviews-api")
    .WithReference(reviewsDb)
    .WaitFor(reviewsDb)
    .WithReference(messaging)          // consumer ProductUpdated
    .WaitFor(messaging)
    .WithReference(identityServer)
    .WithReference(keycloak)
    .WithEnvironment("Jwt__SigningKey", jwtSigningKey)
    .WithHttpHealthCheck(ReadinessPath);

// Демо-сервіс платежів (лише admin) — для перевірки permission-based авторизації Keycloak
var paymentsApi = builder.AddProject<Projects.Payments_Api>("payments-api")
    .WithReference(keycloak)
    .WaitFor(keycloak)
    .WithReference(identityServer)
    .WithEnvironment("Jwt__SigningKey", jwtSigningKey)
    .WithHttpHealthCheck("/health/live");

// ---------- Aggregator (сервіси → Aggregator) ----------
var aggregator = builder.AddProject<Projects.Platform_Aggregator>("aggregator")
    .WithReference(redis).WaitFor(redis)
    .WithReference(ordersApi).WaitFor(ordersApi)
    .WithReference(catalogApi).WaitFor(catalogApi)
    .WithReference(reviewsApi).WaitFor(reviewsApi)
    .WithReference(identityServer).WaitFor(identityServer)   // Client Credentials токен
    .WithEnvironment("IdentityServer__Aggregator__ClientSecret", aggregatorSecret)
    .WithHttpHealthCheck(ReadinessPath);

// ---------- API Gateway (Aggregator → Gateway), фіксований порт ----------
builder.AddProject<Projects.Platform_ApiGateway>("gateway", launchProfileName: null)
    .WithHttpEndpoint(port: 5000, name: "http")
    .WithExternalHttpEndpoints()
    .WithReference(authApi)
    .WithReference(ordersApi)
    .WithReference(catalogApi)
    .WithReference(reviewsApi)
    .WithReference(paymentsApi)
    .WithReference(aggregator)
    .WithReference(identityServer)
    .WithReference(keycloak)
    .WithEnvironment("Jwt__SigningKey", jwtSigningKey)
    .WaitFor(aggregator)
    .WithHttpHealthCheck("/health/live");

builder.Build().Run();
