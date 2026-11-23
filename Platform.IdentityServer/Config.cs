using Duende.IdentityServer;
using Duende.IdentityServer.Models;

namespace Platform.IdentityServer;

/// <summary>
/// Конфігурація IdentityServer: IdentityResources, ApiScopes, ApiResources (по одному на мікросервіс)
/// і три клієнти — postman (Authorization Code), aggregator (Client Credentials), swagger (public + PKCE).
/// Записується в ConfigurationStore (EF) при першому старті — SeedData.
/// </summary>
public static class Config
{
    public static IEnumerable<IdentityResource> IdentityResources =>
    [
        new IdentityResources.OpenId(),
        new IdentityResources.Profile(),
        new IdentityResources.Email(),
        new IdentityResource("roles", "User roles", ["role"])
    ];

    public static IEnumerable<ApiScope> ApiScopes =>
    [
        new("orders.read", "Read orders"),
        new("orders.write", "Create and update orders"),
        new("catalog.read", "Read catalog"),
        new("catalog.write", "Manage catalog"),
        new("reviews.read", "Read reviews"),
        new("reviews.write", "Write reviews"),
        new("payments.read", "Read payments"),
        new("payments.write", "Create payments")
    ];

    /// <summary>ApiResource = audience сервісу; UserClaims "role" потрапляє в access-токен.</summary>
    public static IEnumerable<ApiResource> ApiResources =>
    [
        new("orders-api", "Orders API") { Scopes = { "orders.read", "orders.write" }, UserClaims = { "role", "name" } },
        new("catalog-api", "Catalog API") { Scopes = { "catalog.read", "catalog.write" }, UserClaims = { "role", "name" } },
        new("reviews-api", "Reviews API") { Scopes = { "reviews.read", "reviews.write" }, UserClaims = { "role", "name" } },
        new("payments-api", "Payments API") { Scopes = { "payments.read", "payments.write" }, UserClaims = { "role" } }
    ];

    public static IEnumerable<Client> Clients(IConfiguration configuration) =>
    [
        // 1) Postman — Authorization Code (+PKCE) з секретом, для ручного тестування від імені користувача
        new Client
        {
            ClientId = "postman",
            ClientName = "Postman",
            AllowedGrantTypes = GrantTypes.Code,
            RequirePkce = true,
            ClientSecrets = { new Secret((configuration["Clients:Postman:Secret"] ?? "postman-dev-secret").Sha256()) },
            RedirectUris = { "https://oauth.pstmn.io/v1/callback" },
            AllowOfflineAccess = true,
            AllowedScopes =
            {
                IdentityServerConstants.StandardScopes.OpenId, IdentityServerConstants.StandardScopes.Profile, "roles",
                "orders.read", "orders.write", "catalog.read", "catalog.write", "reviews.read", "reviews.write"
            }
        },
        // 2) Aggregator — Client Credentials: сервіс-сервіс без користувача
        new Client
        {
            ClientId = "aggregator",
            ClientName = "Platform Aggregator",
            AllowedGrantTypes = GrantTypes.ClientCredentials,
            ClientSecrets = { new Secret((configuration["Clients:Aggregator:Secret"] ?? "aggregator-dev-secret").Sha256()) },
            AllowedScopes = { "orders.read", "catalog.read", "reviews.read" },
            AccessTokenLifetime = 3600
        },
        // 3) Swagger UI — public client (без секрету) з обов'язковим PKCE
        new Client
        {
            ClientId = "swagger",
            ClientName = "Swagger UI",
            AllowedGrantTypes = GrantTypes.Code,
            RequirePkce = true,
            RequireClientSecret = false,
            RedirectUris =
            {
                "https://localhost:7101/swagger/oauth2-redirect.html",
                "https://localhost:7102/swagger/oauth2-redirect.html",
                "https://localhost:7103/swagger/oauth2-redirect.html"
            },
            AllowedCorsOrigins = { "https://localhost:7101", "https://localhost:7102", "https://localhost:7103" },
            AllowedScopes = { IdentityServerConstants.StandardScopes.OpenId, "orders.read", "orders.write", "catalog.read", "catalog.write", "reviews.read", "reviews.write" }
        }
    ];
}
