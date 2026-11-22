using Reviews.Application;
using Reviews.Infrastructure;
using Reviews.WebApi.Infrastructure;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();                                     // Serilog, OpenTelemetry, CorrelationId, discovery, resilience
builder.Services.AddApplication();                                // MediatR + behaviors + валідатори + AutoMapper
builder.Services.AddInfrastructure(builder.Configuration);        // MongoDB (ConnectionStrings:ReviewsDb / секція MongoDb)
builder.Services.AddAutoMapper(cfg => { }, typeof(Program).Assembly); // request → command

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

app.MapControllers();
app.MapDefaultEndpoints();
app.Run();
