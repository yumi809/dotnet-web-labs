using System.IdentityModel.Tokens.Jwt;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace Platform.ServiceDefaults.Authorization;

/// <summary>
/// Три джерела токенів платформи (Лаб. 09):
/// <list type="bullet">
/// <item><b>Bearer</b> — Auth Service (ASP.NET Identity + JWT, симетричний ключ);</item>
/// <item><b>IdentityServer</b> — Duende IdentityServer (Authority + Audience, scopes);</item>
/// <item><b>Keycloak</b> — realm bookhub (реєструється в Program.cs сервісу через AddKeycloakJwtBearer).</item>
/// </list>
/// Policy-схема "platform" обирає потрібну схему за issuer токена.
/// </summary>
public static class PlatformAuthentication
{
    public const string PlatformScheme = "platform";
    public const string AuthServiceScheme = JwtBearerDefaults.AuthenticationScheme;   // "Bearer"
    public const string IdentityServerScheme = "IdentityServer";
    public const string KeycloakScheme = "Keycloak";
    public const string KeycloakRealm = "bookhub";

    /// <summary>OAuth scopes IdentityServer; для кожного створюється однойменна політика (scope-based авторизація).</summary>
    public static readonly string[] Scopes =
        ["orders.read", "orders.write", "catalog.read", "catalog.write", "reviews.read", "reviews.write", "payments.read", "payments.write"];

    /// <param name="audience">ApiResource сервісу; null — без перевірки audience (Gateway приймає токени для всіх сервісів).</param>
    public static AuthenticationBuilder AddPlatformAuthentication(this IHostApplicationBuilder builder, string? audience)
    {
        var config = builder.Configuration;
        var jwtIssuer = config["Jwt:Issuer"] ?? "bookhub-auth";
        // Ключ підпису — секрет (AppHost-параметр jwt-signing-key / Kubernetes Secret); у репозиторії його немає
        var signingKey = config["Jwt:SigningKey"]
                         ?? (builder.Environment.IsDevelopment()
                             ? Convert.ToBase64String(Guid.NewGuid().ToByteArray()) + Convert.ToBase64String(Guid.NewGuid().ToByteArray())
                             : throw new InvalidOperationException("Jwt:SigningKey is not configured"));

        var auth = builder.Services.AddAuthentication(PlatformScheme)
            .AddPolicyScheme(PlatformScheme, "Platform JWT", o => o.ForwardDefaultSelector = context =>
            {
                var header = context.Request.Headers.Authorization.ToString();
                if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return AuthServiceScheme;
                var handler = new JwtSecurityTokenHandler();
                var raw = header["Bearer ".Length..].Trim();
                if (!handler.CanReadToken(raw)) return AuthServiceScheme;
                var issuer = handler.ReadJwtToken(raw).Issuer;
                if (issuer == jwtIssuer) return AuthServiceScheme;
                return issuer.Contains("/realms/", StringComparison.Ordinal) ? KeycloakScheme : IdentityServerScheme;
            })
            // 1) Auth Service: валідація підпису, issuer, audience, часу життя
            .AddJwtBearer(AuthServiceScheme, o =>
            {
                o.MapInboundClaims = false;
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwtIssuer,
                    ValidAudience = config["Jwt:Audience"] ?? "bookhub-api",
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = "username",
                    RoleClaimType = "role"
                };
            })
            // 2) Duende IdentityServer: Authority (через service discovery) + Audience = ApiResource сервісу
            .AddJwtBearer(IdentityServerScheme, o =>
            {
                o.Authority = config["IdentityServer:Authority"]
                              ?? config["services:identityserver:https:0"]
                              ?? config["services:identityserver:http:0"];
                o.Audience = audience;
                o.TokenValidationParameters.ValidateAudience = audience is not null;
                o.MapInboundClaims = false;
                o.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
                o.TokenValidationParameters.NameClaimType = "name";
                o.TokenValidationParameters.RoleClaimType = "role";
            });

        builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
        builder.Services.AddAuthorization(o =>
        {
            // scope-based політики: токен IdentityServer має містити відповідний scope
            foreach (var scope in Scopes)
                o.AddPolicy(scope, p => p.RequireAuthenticatedUser().RequireAssertion(ctx =>
                    ctx.User.FindAll("scope").SelectMany(c => c.Value.Split(' ')).Contains(scope)));
        });
        return auth;
    }
}
