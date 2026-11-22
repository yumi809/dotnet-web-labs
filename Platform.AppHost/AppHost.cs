var builder = DistributedApplication.CreateBuilder(args);

// SQL Server у контейнері; ім'я бази "OrdersDb" = ім'я рядка підключення ConnectionStrings:OrdersDb в Orders.Api
var sql = builder.AddSqlServer("sql");
var ordersDb = sql.AddDatabase("OrdersDb");

builder.AddProject<Projects.Orders_Api>("orders-api")
    .WithReference(ordersDb)
    .WaitFor(ordersDb);

builder.Build().Run();
