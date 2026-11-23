using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Platform.ServiceDefaults.Authorization;

/// <summary>Каталог дозволів платформи у форматі resource:action (збігається з scopes resource server-ів у Keycloak).</summary>
public static class Permissions
{
    public const string ClaimType = "authorization.permissions";

    public static class Orders
    {
        public const string Read = "orders:read";
        public const string Create = "orders:create";
        public const string Update = "orders:update";
        public const string Delete = "orders:delete";
    }

    public static class Products
    {
        public const string Read = "products:read";
        public const string Create = "products:create";
        public const string Update = "products:update";
        public const string Delete = "products:delete";
    }

    public static class Payments
    {
        public const string Read = "payments:read";
        public const string Create = "payments:create";
        public const string Refund = "payments:refund";
    }

    /// <summary>
    /// Матриця ролей (та сама, що описана в infra/keycloak): admin — усе; manager — замовлення й товари;
    /// user — читання товарів і власні замовлення; guest — лише перегляд каталогу. Платежі — лише admin.
    /// Використовується як fallback для токенів Auth Service / IdentityServer, де немає claim authorization.permissions.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string[]> RoleMatrix = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["admin"] = [Orders.Read, Orders.Create, Orders.Update, Orders.Delete, Products.Read, Products.Create, Products.Update, Products.Delete,
                     Payments.Read, Payments.Create, Payments.Refund],
        ["manager"] = [Orders.Read, Orders.Create, Orders.Update, Orders.Delete, Products.Read, Products.Create, Products.Update, Products.Delete],
        ["user"] = [Orders.Read, Orders.Create, Products.Read],
        ["guest"] = [Products.Read]
    };

    /// <summary>OAuth scopes IdentityServer → дозволи (для Client Credentials токенів сервісів, напр. Aggregator).</summary>
    public static readonly IReadOnlyDictionary<string, string[]> ScopeMatrix = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["orders.read"] = [Orders.Read],
        ["orders.write"] = [Orders.Create, Orders.Update, Orders.Delete],
        ["catalog.read"] = [Products.Read],
        ["catalog.write"] = [Products.Create, Products.Update, Products.Delete],
        ["payments.read"] = [Payments.Read],
        ["payments.write"] = [Payments.Create, Payments.Refund]
    };
}

/// <summary>[RequirePermission("orders:read")] — permission-based авторизація ендпоінта.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequirePermissionAttribute(string permission) : AuthorizeAttribute(PermissionPolicyProvider.Prefix + permission)
{
    public string Permission { get; } = permission;
}

public sealed record PermissionRequirement(string Permission) : IAuthorizationRequirement;

/// <summary>
/// Перевіряє дозвіл: (1) claim authorization.permissions з токена Keycloak (масив або окремі claims),
/// (2) scopes IdentityServer, (3) ролі за матрицею (токени Auth Service).
/// </summary>
public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        var user = context.User;
        if (user.Identity?.IsAuthenticated != true) return Task.CompletedTask;

        var granted = ReadPermissions(user)
            .Concat(ReadScopes(user).SelectMany(s => Permissions.ScopeMatrix.TryGetValue(s, out var p) ? p : []))
            .Concat(ReadRoles(user).SelectMany(r => Permissions.RoleMatrix.TryGetValue(r, out var p) ? p : []));

        if (granted.Contains(requirement.Permission, StringComparer.OrdinalIgnoreCase))
            context.Succeed(requirement);
        return Task.CompletedTask;
    }

    private static IEnumerable<string> ReadPermissions(ClaimsPrincipal user) =>
        user.FindAll(Permissions.ClaimType).SelectMany(c => Expand(c.Value));

    private static IEnumerable<string> ReadScopes(ClaimsPrincipal user) =>
        user.FindAll("scope").SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static IEnumerable<string> ReadRoles(ClaimsPrincipal user) =>
        user.FindAll("role").Concat(user.FindAll(ClaimTypes.Role)).Concat(user.FindAll("roles")).SelectMany(c => Expand(c.Value));

    /// <summary>Значення claim може бути JSON-масивом (["a","b"]) або одиночним рядком.</summary>
    private static IEnumerable<string> Expand(string value)
    {
        if (!value.StartsWith('[')) return [value];
        try { return JsonSerializer.Deserialize<string[]>(value) ?? []; }
        catch (JsonException) { return [value]; }
    }
}

/// <summary>Динамічно створює політику "perm:&lt;permission&gt;" для будь-якого [RequirePermission].</summary>
public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
{
    public const string Prefix = "perm:";

    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!policyName.StartsWith(Prefix, StringComparison.Ordinal)) return await base.GetPolicyAsync(policyName);
        return new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(policyName[Prefix.Length..]))
            .Build();
    }
}
