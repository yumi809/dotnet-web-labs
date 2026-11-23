using KC = Pulumi.Keycloak;
using OpenId = Pulumi.Keycloak.OpenId;

namespace BookHub.Keycloak;

/// <summary>Extension-методи для створення клієнтів-споживачів токенів і підключення до них mapper-ів.</summary>
public static class ClientExtensions
{
    /// <summary>Authorization Code + PKCE, public (SPA / Swagger / Postman від імені користувача).</summary>
    public static OpenId.Client AddAuthorizationCodeClient(this KC.Realm realm, string clientId, string redirectUri) =>
        new(clientId, new OpenId.ClientArgs
        {
            RealmId = realm.Id,
            ClientId = clientId,
            Name = "BookHub Web App",
            Enabled = true,
            AccessType = "PUBLIC",
            StandardFlowEnabled = true,
            DirectAccessGrantsEnabled = true,      // зручно для тестів (password grant у Postman)
            PkceCodeChallengeMethod = "S256",
            ValidRedirectUris = { redirectUri, "https://oauth.pstmn.io/v1/callback" },
            WebOrigins = { "+" }
        });

    /// <summary>Client Credentials, confidential (сервіс-сервіс, напр. Aggregator).</summary>
    public static OpenId.Client AddClientCredentialsClient(this KC.Realm realm, string clientId) =>
        new(clientId, new OpenId.ClientArgs
        {
            RealmId = realm.Id,
            ClientId = clientId,
            Name = "BookHub Aggregator (service account)",
            Enabled = true,
            AccessType = "CONFIDENTIAL",
            ServiceAccountsEnabled = true,
            StandardFlowEnabled = false
        });

    /// <summary>
    /// Mapper-и для клієнта-споживача: claim authorization.permissions (клієнтські ролі resource server-а,
    /// multivalued) і audience кожного resource server-а — їх перевіряють мікросервіси (AddKeycloakJwtBearer, Audience).
    /// </summary>
    public static void AddPermissionMappers(this OpenId.Client client, KC.Realm realm, string name, IEnumerable<ResourceServer> servers)
    {
        foreach (var server in servers)
        {
            new OpenId.UserClientRoleProtocolMapper($"{name}-permissions-{server.Spec.ClientId}", new OpenId.UserClientRoleProtocolMapperArgs
            {
                RealmId = realm.Id,
                ClientId = client.Id,
                Name = $"permissions-{server.Spec.ClientId}",
                ClaimName = "authorization.permissions",
                ClientIdForRoleMappings = server.Spec.ClientId,
                Multivalued = true,
                AddToAccessToken = true,
                AddToIdToken = false,
                AddToUserinfo = true
            });

            new OpenId.AudienceProtocolMapper($"{name}-audience-{server.Spec.ClientId}", new OpenId.AudienceProtocolMapperArgs
            {
                RealmId = realm.Id,
                ClientId = client.Id,
                Name = $"audience-{server.Spec.ClientId}",
                IncludedClientAudience = server.Spec.ClientId,
                AddToAccessToken = true,
                AddToIdToken = false
            });
        }
    }
}
