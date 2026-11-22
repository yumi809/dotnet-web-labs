var builder = DistributedApplication.CreateBuilder(args);

// ---------- Інфраструктура ----------
// Пароль SQL Server — параметр-секрет (user-secrets: Parameters:sql-password або запит у Dashboard)
var sqlPassword = builder.AddParameter("sql-password", secret: true);

var sql = builder.AddSqlServer("sql", sqlPassword)
    .WithDataVolume("bookhub-sql-data")          // постійне сховище між перезапусками
    .WithLifetime(ContainerLifetime.Persistent);
var ordersDb = sql.AddDatabase("OrdersDb");      // = ConnectionStrings:OrdersDb в Orders.Api
var catalogDb = sql.AddDatabase("CatalogDb");    // = ConnectionStrings:CatalogDb в Catalog.Api

var mongo = builder.AddMongoDB("mongo")
    .WithDataVolume("bookhub-mongo-data")
    .WithLifetime(ContainerLifetime.Persistent);
var reviewsDb = mongo.AddDatabase("ReviewsDb");  // = ConnectionStrings:ReviewsDb в Reviews.WebApi

// Redis — L2-кеш Каталогу і кеш агрегованих відповідей Aggregator
var redis = builder.AddRedis("redis")
    .WithDataVolume("bookhub-redis-data")
    .WithLifetime(ContainerLifetime.Persistent);

// ---------- Мікросервіси (БД → сервіси) ----------
var ordersApi = builder.AddProject<Projects.Orders_Api>("orders-api")
    .WithReference(ordersDb)
    .WaitFor(ordersDb);

var catalogApi = builder.AddProject<Projects.Catalog_Api>("catalog-api")
    .WithReference(catalogDb)
    .WaitFor(catalogDb)
    .WithReference(redis)
    .WaitFor(redis);

var reviewsApi = builder.AddProject<Projects.Reviews_WebApi>("reviews-api")
    .WithReference(reviewsDb)
    .WaitFor(reviewsDb);

// ---------- Aggregator (сервіси → Aggregator) ----------
var aggregator = builder.AddProject<Projects.Platform_Aggregator>("aggregator")
    .WithReference(redis).WaitFor(redis)
    .WithReference(ordersApi).WaitFor(ordersApi)
    .WithReference(catalogApi).WaitFor(catalogApi)
    .WithReference(reviewsApi).WaitFor(reviewsApi);

// ---------- API Gateway (Aggregator → Gateway), фіксований порт ----------
builder.AddProject<Projects.Platform_ApiGateway>("gateway", launchProfileName: null)
    .WithHttpEndpoint(port: 5000, name: "http")
    .WithExternalHttpEndpoints()
    .WithReference(ordersApi)
    .WithReference(catalogApi)
    .WithReference(reviewsApi)
    .WithReference(aggregator)
    .WaitFor(aggregator);

builder.Build().Run();
