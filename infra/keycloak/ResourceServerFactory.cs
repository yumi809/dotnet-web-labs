using Pulumi;
using KC = Pulumi.Keycloak;
using OpenId = Pulumi.Keycloak.OpenId;

namespace BookHub.Keycloak;

/// <summary>Створений resource server: клієнт, клієнтські ролі-дозволи (resource:scope) і scopes авторизації.</summary>
public sealed record ResourceServer(ResourceServerSpec Spec, OpenId.Client Client, IReadOnlyDictionary<string, KC.Role> PermissionRoles);

/// <summary>
/// Фабрика resource server-ів: confidential-клієнт з увімкненими Authorization Services,
/// повний набір scopes, ресурс, клієнтські ролі на кожен дозвіл, role-політики та scope-permissions за матрицею.
/// </summary>
public static class ResourceServerFactory
{
    public static ResourceServer Create(KC.Realm realm, ResourceServerSpec spec)
    {
        var client = new OpenId.Client(spec.ClientId, new OpenId.ClientArgs
        {
            RealmId = realm.Id,
            ClientId = spec.ClientId,
            Name = spec.Name,
            Enabled = true,
            AccessType = "CONFIDENTIAL",
            ServiceAccountsEnabled = true,           // обов'язково для Authorization Services
            StandardFlowEnabled = false,
            Authorization = new OpenId.Inputs.ClientAuthorizationArgs
            {
                PolicyEnforcementMode = "ENFORCING",
                DecisionStrategy = "AFFIRMATIVE"
            }
        });

        // Scopes resource server-а: read / create / update / delete (payments: read / create / refund)
        var scopes = spec.Scopes.ToDictionary(s => s, s => new OpenId.ClientAuthorizationScope($"{spec.ClientId}-scope-{s}",
            new OpenId.ClientAuthorizationScopeArgs
            {
                RealmId = realm.Id,
                ResourceServerId = client.ResourceServerId,
                Name = $"{spec.Resource}:{s}"
            }));

        var resource = new OpenId.ClientAuthorizationResource($"{spec.ClientId}-resource", new OpenId.ClientAuthorizationResourceArgs
        {
            RealmId = realm.Id,
            ResourceServerId = client.ResourceServerId,
            Name = spec.Resource,
            DisplayName = spec.Name,
            Uris = { $"/api/{spec.Resource}/*" },
            Scopes = scopes.Values.Select(s => s.Name).ToList()
        });

        // Клієнтська роль на кожен дозвіл (orders:read ...) — потрапляє в токен claim-ом authorization.permissions
        var permissionRoles = spec.Permissions.ToDictionary(p => p, p => new KC.Role($"{spec.ClientId}-role-{p.Replace(':', '-')}",
            new KC.RoleArgs
            {
                RealmId = realm.Id,
                ClientId = client.Id,
                Name = p,
                Description = $"Permission {p}"
            }));

        // Role-політика і scope-permission на кожен дозвіл (оцінюються Policy Enforcer-ом / UMA)
        foreach (var (permission, role) in permissionRoles)
        {
            var policy = new OpenId.ClientRolePolicy($"{spec.ClientId}-policy-{permission.Replace(':', '-')}", new OpenId.ClientRolePolicyArgs
            {
                RealmId = realm.Id,
                ResourceServerId = client.ResourceServerId,
                Name = $"has-{permission}",
                Type = "role",
                Logic = "POSITIVE",
                DecisionStrategy = "UNANIMOUS",
                Roles = { new OpenId.Inputs.ClientRolePolicyRoleArgs { Id = role.Id, Required = true } }
            });

            var scopeName = permission.Split(':')[1];
            new OpenId.ClientAuthorizationPermission($"{spec.ClientId}-permission-{permission.Replace(':', '-')}",
                new OpenId.ClientAuthorizationPermissionArgs
                {
                    RealmId = realm.Id,
                    ResourceServerId = client.ResourceServerId,
                    Name = $"permission-{permission}",
                    Type = "scope",
                    DecisionStrategy = "AFFIRMATIVE",
                    Resources = { resource.Id },
                    Scopes = { scopes[scopeName].Id },
                    Policies = { policy.Id }
                });
        }

        return new ResourceServer(spec, client, permissionRoles);
    }
}
