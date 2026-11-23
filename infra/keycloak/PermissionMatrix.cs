namespace BookHub.Keycloak;

/// <summary>
/// Матриця доступу платформи. Resource server = мікросервіс, scope = дія над ресурсом.
/// Той самий каталог дозволів використовує Platform.ServiceDefaults/Authorization/Permissions.cs.
/// </summary>
public static class PermissionMatrix
{
    public static readonly ResourceServerSpec[] ResourceServers =
    [
        new("orders-api", "Orders API", "orders", ["read", "create", "update", "delete"]),
        new("products-api", "Products (Catalog) API", "products", ["read", "create", "update", "delete"]),
        new("payments-api", "Payments API", "payments", ["read", "create", "refund"])
    ];

    public static readonly string[] Roles = ["admin", "manager", "user", "guest"];

    /// <summary>
    /// Роль → дозволи (resource:scope). Платежі доступні ЛИШЕ admin.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string[]> RolePermissions = new Dictionary<string, string[]>
    {
        ["admin"] =
        [
            "orders:read", "orders:create", "orders:update", "orders:delete",
            "products:read", "products:create", "products:update", "products:delete",
            "payments:read", "payments:create", "payments:refund"
        ],
        ["manager"] =
        [
            "orders:read", "orders:create", "orders:update", "orders:delete",
            "products:read", "products:create", "products:update", "products:delete"
        ],
        ["user"] = ["orders:read", "orders:create", "products:read"],
        ["guest"] = ["products:read"]
    };
}

public sealed record ResourceServerSpec(string ClientId, string Name, string Resource, string[] Scopes)
{
    public IEnumerable<string> Permissions => Scopes.Select(s => $"{Resource}:{s}");
}
