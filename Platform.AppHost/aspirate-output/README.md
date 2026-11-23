# aspirate-output — маніфести всієї платформи, згенеровані Aspir8 (aspirate)

Згенеровано з AppHost (Aspire manifest) командою:

```bash
dotnet tool restore
dotnet run --project Platform.AppHost -- --publisher manifest --output-path ../aspire-manifest.json
dotnet aspirate generate --skip-build --non-interactive --disable-secrets --disable-state \
  --include-dashboard false --image-pull-policy IfNotPresent --container-registry docker.io/yumi809 \
  --output-format kustomize --output-path ./Platform.AppHost/aspirate-output -m ./aspire-manifest.json
```

Структура (kustomize): на кожен ресурс — `deployment.yaml`/`statefulset.yaml`, `service.yaml`, `kustomization.yaml`
з `configMapGenerator` (`<ім'я>-env`):

* сервіси: `orders-api`, `catalog-api`, `reviews-api`, `auth-api`, `payments-api`, `identityserver`, `aggregator`, `gateway`;
* інфраструктура: `sql`, `mongo`, `redis`, `messaging` (RabbitMQ), `keycloak`.

**Секретів у репозиторії немає.** Значення, що містять паролі (рядки підключення, `*_PASSWORD`, `*_URI`, `Jwt__SigningKey`,
секрети клієнтів), прибрано з `configMapGenerator`; кожен Deployment/StatefulSet додатково підключає
`envFrom.secretRef: <ім'я>-secrets`. Створіть ці Secret-и перед `kubectl apply -k`:

```bash
kubectl create secret generic sql-secrets --from-literal=MSSQL_SA_PASSWORD='<SQL_PASSWORD>'
kubectl create secret generic redis-secrets --from-literal=REDIS_PASSWORD='<REDIS_PASSWORD>'
kubectl create secret generic messaging-secrets --from-literal=RABBITMQ_DEFAULT_PASS='<RABBITMQ_PASSWORD>'
kubectl create secret generic mongo-secrets --from-literal=MONGO_INITDB_ROOT_PASSWORD='<MONGO_PASSWORD>'
kubectl create secret generic keycloak-secrets --from-literal=KC_BOOTSTRAP_ADMIN_PASSWORD='<KEYCLOAK_ADMIN_PASSWORD>'

kubectl create secret generic orders-api-secrets \
  --from-literal=ConnectionStrings__OrdersDb='Server=sql,1433;User ID=sa;Password=<SQL_PASSWORD>;TrustServerCertificate=true;Initial Catalog=OrdersDb' \
  --from-literal=ConnectionStrings__messaging='amqp://guest:<RABBITMQ_PASSWORD>@messaging:5672' \
  --from-literal=Jwt__SigningKey='<JWT_SIGNING_KEY>'
kubectl create secret generic catalog-api-secrets \
  --from-literal=ConnectionStrings__CatalogDb='Server=sql,1433;User ID=sa;Password=<SQL_PASSWORD>;TrustServerCertificate=true;Initial Catalog=CatalogDb' \
  --from-literal=ConnectionStrings__redis='redis:6379,password=<REDIS_PASSWORD>' \
  --from-literal=ConnectionStrings__messaging='amqp://guest:<RABBITMQ_PASSWORD>@messaging:5672' \
  --from-literal=Jwt__SigningKey='<JWT_SIGNING_KEY>'
kubectl create secret generic reviews-api-secrets \
  --from-literal=ConnectionStrings__ReviewsDb='mongodb://admin:<MONGO_PASSWORD>@mongo:27017/ReviewsDb?authSource=admin' \
  --from-literal=ConnectionStrings__messaging='amqp://guest:<RABBITMQ_PASSWORD>@messaging:5672' \
  --from-literal=Jwt__SigningKey='<JWT_SIGNING_KEY>'
kubectl create secret generic auth-api-secrets \
  --from-literal=ConnectionStrings__IdentityDb='Server=sql,1433;User ID=sa;Password=<SQL_PASSWORD>;TrustServerCertificate=true;Initial Catalog=IdentityDb' \
  --from-literal=Jwt__SigningKey='<JWT_SIGNING_KEY>' --from-literal=Seed__AdminPassword='<SEED_PASSWORD>'
kubectl create secret generic identityserver-secrets \
  --from-literal=ConnectionStrings__IdentityServerDb='Server=sql,1433;User ID=sa;Password=<SQL_PASSWORD>;TrustServerCertificate=true;Initial Catalog=IdentityServerDb' \
  --from-literal=Clients__Aggregator__Secret='<AGGREGATOR_SECRET>' --from-literal=Seed__UserPassword='<SEED_PASSWORD>'
kubectl create secret generic aggregator-secrets \
  --from-literal=ConnectionStrings__redis='redis:6379,password=<REDIS_PASSWORD>' \
  --from-literal=IdentityServer__Aggregator__ClientSecret='<AGGREGATOR_SECRET>'
kubectl create secret generic payments-api-secrets --from-literal=Jwt__SigningKey='<JWT_SIGNING_KEY>'
kubectl create secret generic gateway-secrets --from-literal=Jwt__SigningKey='<JWT_SIGNING_KEY>'

kubectl apply -k Platform.AppHost/aspirate-output
```
