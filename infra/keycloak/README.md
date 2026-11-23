# infra/keycloak — Pulumi (C#) для realm `bookhub`

Keycloak піднімає Aspire AppHost (`AddKeycloak("keycloak", 8080).WithDataVolume()`); цей проєкт описує його вміст як код.

| Що створюється | Де |
|---|---|
| Realm `bookhub` (TTL access-токена 15 хв, політика паролів) | `Program.cs` |
| Resource servers `orders-api`, `products-api`, `payments-api` з Authorization Services: scopes (`read/create/update/delete`, для платежів `read/create/refund`), ресурс, role-політики, scope-permissions | `ResourceServerFactory.cs` |
| Клієнтські ролі-дозволи (`orders:read` …) і realm-ролі `admin / manager / user / guest` як composite за матрицею | `PermissionMatrix.cs`, `Program.cs` |
| Клієнти: `bookhub-web` (Authorization Code + PKCE), `aggregator-service` (Client Credentials) — extension-методи | `ClientExtensions.cs` |
| Mapper `authorization.permissions` (multivalued, клієнтські ролі кожного resource server-а) і audience-mapper-и | `ClientExtensions.AddPermissionMappers` |
| Тестові користувачі `admin.test`, `manager.test`, `user.test`, `guest.test` | `Program.cs` |
| Outputs: realm, issuer, clientId-и, секрет aggregator (secret) | `Program.cs` |

Матриця: **admin** — усе; **manager** — замовлення й товари; **user** — `orders:read`, `orders:create`, `products:read`;
**guest** — `products:read`. Платежі (`payments:*`) — **лише admin**.

```bash
cd infra/keycloak
pulumi stack init dev
pulumi config set --secret keycloak:password <keycloak-admin-password>     # той самий, що Parameters:keycloak-admin-password в AppHost
pulumi config set --secret bookhub:testUserPassword <Passw0rd!>
pulumi up
pulumi stack output aggregatorClientSecret --show-secrets
```

Перевірка (password grant для тестів):

```bash
curl -X POST http://localhost:8080/realms/bookhub/protocol/openid-connect/token \
  -d grant_type=password -d client_id=bookhub-web -d username=user.test -d password=<Passw0rd!>
# у access_token: "authorization.permissions": ["orders:read","orders:create","products:read"], aud: orders-api, products-api, payments-api
```
