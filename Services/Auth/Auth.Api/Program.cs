using System.Text;
using Auth.Api.Controllers;
using Auth.Api.Data;
using Auth.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

// Окрема база IdentityDb (ресурс AppHost → ConnectionStrings:IdentityDb)
var identityConnection = builder.Configuration.GetConnectionString("IdentityDb");
builder.Services.AddDbContext<AuthDbContext>(o => o.UseSqlServer(identityConnection));

// ASP.NET Identity: вимоги до пароля, lockout, обов'язкове підтвердження email
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(o =>
    {
        o.Password.RequiredLength = 8;
        o.Password.RequireDigit = true;
        o.Password.RequireUppercase = true;
        o.Password.RequireLowercase = true;
        o.Password.RequireNonAlphanumeric = true;
        o.Lockout.MaxFailedAccessAttempts = 5;
        o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        o.Lockout.AllowedForNewUsers = true;
        o.User.RequireUniqueEmail = true;
        o.SignIn.RequireConfirmedEmail = true;
    })
    .AddEntityFrameworkStores<AuthDbContext>()
    .AddDefaultTokenProviders();

// JWT: access 15 хв (claims sub, username, role), refresh 7 днів з ротацією
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()!;
// AddIdentity реєструє cookie-схеми — для API схемою за замовчуванням робимо JWT Bearer
builder.Services.AddAuthentication(o =>
    {
        o.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
        o.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        o.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(o =>
    {
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "username",
            RoleClaimType = "role"
        };
    });
builder.Services.AddAuthorization();

builder.Services.AddScoped<TokenService>();
builder.Services.AddSingleton<IAuthEmailSender, LoggingEmailSender>();
builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseServiceDefaults();
app.UseSerilogRequestLogging();
app.UseExceptionHandler();

// Міграції Identity + ролі User/Admin + адміністратор за замовчуванням (пароль — з конфігурації/секретів)
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
    await db.Database.MigrateAsync();
    var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    foreach (var role in Roles.All)
        if (!await roles.RoleExistsAsync(role)) await roles.CreateAsync(new IdentityRole(role));

    var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var adminEmail = app.Configuration["Seed:AdminEmail"] ?? "admin@bookhub.local";
    var adminPassword = app.Configuration["Seed:AdminPassword"];
    if (!string.IsNullOrEmpty(adminPassword) && await users.FindByEmailAsync(adminEmail) is null)
    {
        var admin = new ApplicationUser { UserName = "admin", Email = adminEmail, FullName = "BookHub Admin", EmailConfirmed = true };
        if ((await users.CreateAsync(admin, adminPassword)).Succeeded)
            await users.AddToRolesAsync(admin, Roles.All);
    }
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapDefaultEndpoints();
app.Run();
