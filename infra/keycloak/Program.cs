using BookHub.Keycloak;
using Pulumi;
using KC = Pulumi.Keycloak;
using OpenId = Pulumi.Keycloak.OpenId;

return await Deployment.RunAsync(() =>
{
    var config = new Config("bookhub");
    var testUserPassword = config.RequireSecret("testUserPassword");
    var redirectUri = config.Get("webAppRedirectUri") ?? "http://localhost:5000/*";

    // ---------- Realm ----------
    var realm = new KC.Realm("bookhub", new KC.RealmArgs
    {
        RealmName = "bookhub",
        DisplayName = "BookHub",
        Enabled = true,
        AccessTokenLifespan = "15m",
        SsoSessionIdleTimeout = "30m",
        RegistrationAllowed = false,
        LoginWithEmailAllowed = true,
        PasswordPolicy = "length(8) and digits(1) and upperCase(1) and notUsername"
    });

    // ---------- Resource servers зі scopes (orders-api, products-api, payments-api) ----------
    var servers = PermissionMatrix.ResourceServers.Select(spec => ResourceServerFactory.Create(realm, spec)).ToList();
    var permissionRoles = servers.SelectMany(s => s.PermissionRoles).ToDictionary(p => p.Key, p => p.Value);

    // ---------- Realm-ролі за матрицею: composite з клієнтських ролей-дозволів ----------
    var roles = PermissionMatrix.Roles.ToDictionary(role => role, role => new KC.Role($"role-{role}", new KC.RoleArgs
    {
        RealmId = realm.Id,
        Name = role,
        Description = $"BookHub {role}",
        CompositeRoles = PermissionMatrix.RolePermissions[role].Select(p => permissionRoles[p].Id).ToList()
    }));

    // ---------- Клієнти (через extension-методи) ----------
    var webApp = realm.AddAuthorizationCodeClient("bookhub-web", redirectUri);
    webApp.AddPermissionMappers(realm, "web", servers);

    var aggregator = realm.AddClientCredentialsClient("aggregator-service");
    aggregator.AddPermissionMappers(realm, "aggregator", servers);
    // service account Aggregator — роль user (читання замовлень і товарів)
    new OpenId.ClientServiceAccountRealmRole("aggregator-sa-role", new OpenId.ClientServiceAccountRealmRoleArgs
    {
        RealmId = realm.Id,
        ServiceAccountUserId = aggregator.ServiceAccountUserId,
        Role = roles["user"].Name
    });

    // ---------- Тестові користувачі: по одному на кожну роль ----------
    var users = PermissionMatrix.Roles.ToDictionary(role => role, role =>
    {
        var user = new KC.User($"user-{role}", new KC.UserArgs
        {
            RealmId = realm.Id,
            Username = $"{role}.test",
            Email = $"{role}.test@bookhub.local",
            EmailVerified = true,
            FirstName = char.ToUpperInvariant(role[0]) + role[1..],
            LastName = "Test",
            Enabled = true,
            InitialPassword = new KC.Inputs.UserInitialPasswordArgs { Value = testUserPassword, Temporary = false }
        });
        new KC.UserRoles($"user-{role}-roles", new KC.UserRolesArgs
        {
            RealmId = realm.Id,
            UserId = user.Id,
            RoleIds = { roles[role].Id }
        });
        return user;
    });

    // ---------- Outputs ----------
    return new Dictionary<string, object?>
    {
        ["realm"] = realm.RealmName,
        ["issuer"] = Output.Format($"http://localhost:8080/realms/{realm.RealmName}"),
        ["resourceServers"] = servers.Select(s => s.Spec.ClientId).ToArray(),
        ["webClientId"] = webApp.ClientId,
        ["aggregatorClientId"] = aggregator.ClientId,
        ["aggregatorClientSecret"] = Output.CreateSecret(aggregator.ClientSecret),
        ["testUsers"] = users.Keys.Select(r => $"{r}.test").ToArray()
    };
});
