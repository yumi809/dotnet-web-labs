using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Platform.IdentityServer;
using Platform.IdentityServer.Data;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

// Окрема база IdentityServerDb (ресурс AppHost): користувачі + ConfigurationStore + OperationalStore
var connectionString = builder.Configuration.GetConnectionString("IdentityServerDb")
                       ?? "Server=localhost,1433;Database=IdentityServerDb;User Id=sa;Password=Your_strong_password1;TrustServerCertificate=True";
var migrationsAssembly = typeof(Program).Assembly.GetName().Name;

builder.Services.AddDbContext<ApplicationDbContext>(o => o.UseSqlServer(connectionString));
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(o =>
    {
        o.Password.RequiredLength = 8;
        o.Lockout.MaxFailedAccessAttempts = 5;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.AddRazorPages();

builder.Services.AddIdentityServer(o =>
    {
        o.Events.RaiseErrorEvents = true;
        o.Events.RaiseFailureEvents = true;
        o.Events.RaiseSuccessEvents = true;
        o.EmitStaticAudienceClaim = true;
    })
    // клієнти, ресурси, scopes — у БД (міграції ConfigurationDbContext)
    .AddConfigurationStore(o => o.ConfigureDbContext = b =>
        b.UseSqlServer(connectionString, sql => sql.MigrationsAssembly(migrationsAssembly)))
    // гранти, refresh-токени, ключі — у БД (міграції PersistedGrantDbContext)
    .AddOperationalStore(o =>
    {
        o.ConfigureDbContext = b => b.UseSqlServer(connectionString, sql => sql.MigrationsAssembly(migrationsAssembly));
        o.EnableTokenCleanup = true;
    })
    // користувачі ASP.NET Identity як джерело суб'єктів і claims (ролі → claim "role")
    .AddAspNetIdentity<ApplicationUser>();

var app = builder.Build();

app.UseServiceDefaults();
app.UseSerilogRequestLogging();
app.UseStaticFiles();
app.UseRouting();
app.UseIdentityServer();
app.UseAuthorization();

await app.EnsureSeedDataAsync();

app.MapRazorPages();
app.MapDefaultEndpoints();
app.Run();
