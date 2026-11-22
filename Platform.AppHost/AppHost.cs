var builder = DistributedApplication.CreateBuilder(args);

// SQL Server у контейнері; імена баз = імена рядків підключення сервісів
var sql = builder.AddSqlServer("sql");
var ordersDb = sql.AddDatabase("OrdersDb");
var catalogDb = sql.AddDatabase("CatalogDb");

builder.AddProject<Projects.Orders_Api>("orders-api")
    .WithReference(ordersDb)
    .WaitFor(ordersDb);

builder.AddProject<Projects.Catalog_Api>("catalog-api")
    .WithReference(catalogDb)
    .WaitFor(catalogDb);

builder.Build().Run();
