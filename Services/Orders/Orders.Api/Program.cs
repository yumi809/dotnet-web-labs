using Orders.Api.Infrastructure;
using Orders.Bll;

var builder = WebApplication.CreateBuilder(args);

// Логування (консоль + рівні з appsettings)
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o => { o.IncludeScopes = true; o.TimestampFormat = "HH:mm:ss "; });

// BLL + DAL: Unit of Work і сервіси — Scoped; рядок підключення ConnectionStrings:OrdersDb
builder.Services.AddOrdersBll();

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o => o.SwaggerDoc("v1", new() { Title = "Orders API", Version = "v1" }));

var app = builder.Build();

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
app.Run();
