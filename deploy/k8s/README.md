# Ручне розгортання в Kubernetes (Лаб. 10)

Власні маніфести для `catalog-api` і `gateway` (образи з багатоетапних Dockerfile). Повний набір маніфестів усієї
платформи, згенерований `aspirate`, — у [`Platform.AppHost/aspirate-output`](../../Platform.AppHost/aspirate-output).

| Файл | Що описує |
|---|---|
| `catalog-api-deployment.yaml`, `gateway-deployment.yaml` | `replicas: 2`, `imagePullPolicy: IfNotPresent`, `containerPort: 8080`, `livenessProbe /health/live`, `readinessProbe /health/ready` (endpoint-и Лаб. 07, доступні в усіх середовищах) |
| `catalog-api-service.yaml`, `gateway-service.yaml` | `ClusterIP`, selector за лейблом поду, `port 80 → targetPort 8080` |
| `configmap.yaml` | `bookhub-config`: `services__<ім'я>__http__0` з DNS-іменами Service-ів, несекретні налаштування |

Секрети **не зберігаються в репозиторії**: вони створюються командами `kubectl create secret` і підключаються через
`envFrom.secretRef`.

## 1. Образи

```bash
docker build -f Services/Catalog/Catalog.Api/Dockerfile -t bookhub/catalog-api:1.0 .
docker build -f Platform.ApiGateway/Dockerfile -t bookhub/gateway:1.0 .
# локальний кластер:
kind load docker-image bookhub/catalog-api:1.0 bookhub/gateway:1.0     # або: minikube image load ...
```

## 2. Namespace і секрети

```bash
kubectl create namespace bookhub
kubectl config set-context --current --namespace=bookhub

kubectl create secret generic catalog-api-secrets \
  --from-literal=ConnectionStrings__CatalogDb='Server=sql,1433;Database=CatalogDb;User Id=sa;Password=<SQL_PASSWORD>;TrustServerCertificate=True' \
  --from-literal=ConnectionStrings__redis='redis:6379,password=<REDIS_PASSWORD>' \
  --from-literal=ConnectionStrings__messaging='amqp://guest:<RABBITMQ_PASSWORD>@messaging:5672' \
  --from-literal=Jwt__SigningKey='<JWT_SIGNING_KEY>'

kubectl create secret generic gateway-secrets \
  --from-literal=Jwt__SigningKey='<JWT_SIGNING_KEY>'
```

## 3. Розгортання

```bash
kubectl apply -f deploy/k8s/configmap.yaml
kubectl apply -f deploy/k8s/catalog-api-deployment.yaml -f deploy/k8s/catalog-api-service.yaml
kubectl apply -f deploy/k8s/gateway-deployment.yaml -f deploy/k8s/gateway-service.yaml

kubectl get pods -l app=catalog-api -w          # 2/2 Ready після проходження readinessProbe
kubectl port-forward svc/gateway 5000:80
curl http://localhost:5000/health/live
```

Інфраструктуру (SQL Server, Redis, RabbitMQ, MongoDB, Keycloak) та решту сервісів можна розгорнути з
`Platform.AppHost/aspirate-output` (`kubectl apply -k Platform.AppHost/aspirate-output`) — див. README в тій папці.
