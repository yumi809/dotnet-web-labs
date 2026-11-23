# BookHub — мікросервісна платформа книжкового інтернет-магазину

Курс «Технології .NET розробки web-додатків». Один репозиторій на весь курс: кожна лабораторна — окрема гілка `lab-NN`.

## Предметна область

**BookHub** — інтернет-магазин книг. Покупці переглядають каталог книг, оформлюють і оплачують замовлення,
залишають відгуки й беруть участь в обговореннях. Менеджери ведуть каталог і обробляють замовлення.

Система поділена на три незалежні контексти, кожен з власною базою даних:

| № | Контекст | БД | Технологія | Сутності |
|---|---|---|---|---|
| 1 | Замовлення (`Orders`) | `OrdersDb` — SQL Server | ADO.NET + Dapper, stored procedures | Customers, CustomerProfiles, Orders, OrderItems, OrderStatusHistory, Promotions, OrderPromotions, Payments, Products (копія) |
| 2 | Каталог (`Catalog`) | `CatalogDb` — SQL Server | EF Core Code First | Products, ProductDetails, Categories, ProductCategories, Publishers |
| 3 | Відгуки (`Reviews`) | `ReviewsDb` — MongoDB | MongoDB Driver, CQRS | reviews, discussions, discussionMessages, productRatings |

Детальніше: [docs/contexts.md](docs/contexts.md), ERD — [docs/erd.md](docs/erd.md).

## Політика дублювання даних

Між базами немає FOREIGN KEY: посилання на сутність іншого контексту — звичайний стовпець/поле з її Id.

| Поле | Де зберігається | Джерело (власник) | Політика узгодженості |
|---|---|---|---|
| `OrderItems.ProductName`, `OrderItems.UnitPrice` | OrdersDb | `Products` (CatalogDb) | **знімок** на момент додавання позиції — ніколи не оновлюється |
| `Products.Name`, `Products.Price`, `Products.IsAvailable` (локальний довідник) | OrdersDb | `Products` (CatalogDb) | **копія для читання**, оновлюється подією `ProductUpdated` з Каталогу (Лаб. 08) |
| `reviews.product.productName`, `discussions.product.productName` | ReviewsDb | `Products.Name` (CatalogDb) | копія, оновлюється подією `ProductUpdated` (Лаб. 08) у всіх документах товару |
| `reviews.author.displayName` | ReviewsDb | профіль користувача (Auth) | знімок на момент написання відгуку |
| `productRatings.average`, `reviewsCount` | ReviewsDb | колекція `reviews` (той самий контекст) | матеріалізований підсумок, перераховується при створенні/видаленні відгуку |

Подвійного запису (dual write) немає: кожне поле має одного власника, решта — знімок або копія, що оновлюється подіями.

## Розгортання баз даних

Потрібно: SQL Server 2019+ (або Docker `mcr.microsoft.com/mssql/server:2022-latest`), MongoDB 7+ і `mongosh`.

```bash
# Проєкт №1 — OrdersDb (порядок важливий)
sqlcmd -S localhost -U sa -P <password> -i db/p1/schema.sql
sqlcmd -S localhost -U sa -P <password> -i db/p1/procedures.sql
sqlcmd -S localhost -U sa -P <password> -i db/p1/seed.sql

# Проєкт №2 — CatalogDb (або міграції EF Core, див. Лаб. 03)
sqlcmd -S localhost -U sa -P <password> -i db/p2/schema.sql
sqlcmd -S localhost -U sa -P <password> -i db/p2/seed.sql

# Проєкт №3 — ReviewsDb
mongosh "mongodb://localhost:27017" db/p3/collections.js
mongosh "mongodb://localhost:27017" db/p3/seed.js
```

Усі скрипти ідемпотентні: схема створюється через `IF OBJECT_ID(...) IS NULL`, seed — через `IF NOT EXISTS` / `MERGE` /
`updateOne(..., {upsert: true})`, тож повторний запуск не створює дублікатів.

### Збережувані процедури OrdersDb

| Процедура | Призначення |
|---|---|
| `usp_Order_Create` | створення замовлення + запис в історію статусів |
| `usp_Order_GetWithItems` | замовлення з позиціями (два result set) |
| `usp_Order_AddItem` | додавання позиції зі знімком ціни/назви (транзакція) |
| `usp_Order_ChangeStatus` | **транзакційна** зміна статусу з перевіркою матриці переходів і rowversion; недопустимий перехід → `THROW 50409` |
| `usp_Order_ApplyPromotion` | застосування промокоду (M:N `OrderPromotions`) |
| `usp_Customer_GetOrderStats` | агрегована статистика клієнта |
| `usp_Order_SoftDelete` | м'яке видалення (`IsDeleted = 1`) |

### Індекси

| Індекс | Запит, під який створено |
|---|---|
| `IX_Orders_CustomerId_CreatedAt` (filtered `IsDeleted = 0`, INCLUDE Status, TotalAmount) | список замовлень клієнта, новіші першими |
| `IX_Orders_Status` | черга замовлень менеджера за статусом |
| `IX_OrderItems_OrderId` | завантаження позицій картки замовлення |
| `IX_OrderItems_ProductId` | аналітика продажів товару |
| `IX_OSH_OrderId_ChangedAt` | історія статусів замовлення |
| `IX_Payments_OrderId` | платежі замовлення |
| `IX_Products_Name`, `IX_Products_IsActive_Price` (CatalogDb) | пошук за назвою, фільтр за ціною |
| `IX_ProductCategories_CategoryId_AddedAt` (CatalogDb) | новинки категорії |
| `ix_reviews_product_created`, `ux_reviews_product_author`, `tx_reviews_text` (MongoDB) | відгуки товару; один відгук на користувача; повнотекстовий пошук |

## Запуск через .NET Aspire

Потрібно: .NET 10 SDK, Docker Desktop (запущений).

```bash
dotnet run --project Platform.AppHost
```

AppHost піднімає контейнер SQL Server, створює базу `OrdersDb` (ім'я ресурсу = ім'я рядка підключення
`ConnectionStrings:OrdersDb`) і запускає `orders-api` з `WithReference(ordersDb).WaitFor(ordersDb)`.
У Development `Orders.Api` при старті виконує ідемпотентні скрипти `db/p1/*.sql`. Адреси сервісів — в Aspire Dashboard.

## Платформа на .NET Aspire (Лаб. 05)

```
Platform.AppHost/         оркестрація: SQL Server + MongoDB (WithDataVolume), сервіси, Aggregator, Gateway
Platform.ServiceDefaults/ Extensions.cs: Serilog, OpenTelemetry, CorrelationId, service discovery, resilience, health
Platform.ApiGateway/      YARP: маршрути /api/orders|catalog|reviews|aggregator, CorrelationId, логування маршрутизації
Platform.Aggregator/      typed HttpClients, паралельна агрегація, часткові відповіді з Warnings
```

### Запуск

```bash
# пароль SQL Server — параметр-секрет AppHost
dotnet user-secrets set "Parameters:sql-password" "<StrongPassw0rd!>" --project Platform.AppHost
dotnet run --project Platform.AppHost
```

Порядок старту: `sql`/`mongo` → `orders-api`, `catalog-api`, `reviews-api` → `aggregator` → `gateway`
(`WithReference` + `WaitFor`). Gateway слухає фіксований порт **http://localhost:5000**. Dashboard Aspire відкривається
автоматично (Resources, Structured logs, Traces, Metrics).

### Запити через Gateway

```bash
curl http://localhost:5000/api/catalog/products?page=1&pageSize=5
curl http://localhost:5000/api/orders/1
curl "http://localhost:5000/api/reviews?productId=3"

# Aggregator: картка замовлення (Orders + Catalog + Reviews паралельно)
curl -H "X-Correlation-Id: demo-123" http://localhost:5000/api/aggregator/orders/1/details
# Aggregator: картка книги (Catalog + рейтинг + 3 останні відгуки)
curl http://localhost:5000/api/aggregator/products/3
```

Якщо один із сервісів недоступний, Aggregator повертає 200 з доступними даними та списком `warnings`
(наприклад `"Reviews are unavailable: ratings are not shown"`), а не 500. Обов'язкова частина (замовлення/товар) → 503.

| Наскрізні механізми | Де |
|---|---|
| `X-Correlation-Id`: береться із запиту або генерується, кладеться в `LogContext`, відповідь і вихідні запити (`CorrelationIdDelegatingHandler`) | `Platform.ServiceDefaults/Correlation` |
| Serilog: `ServiceName`, `EnvironmentName`, `MachineName`, `ThreadId`, `FromLogContext`, `CompactJsonFormatter`, `writeToProviders: true` | `Platform.ServiceDefaults/Extensions.cs` |
| OpenTelemetry: ASP.NET Core, HttpClient, SqlClient, MongoDB DiagnosticSources, власні `ActivitySource` `BookHub.*`, `UseOtlpExporter` | `Platform.ServiceDefaults/Extensions.cs` |
| Власні spans бізнес-операцій | `OrdersTelemetry`, `CatalogTelemetry`, `ReviewsTelemetry` |

## gRPC і дворівневе кешування (Лаб. 06)

| Компонент | Реалізація |
|---|---|
| Redis | `AddRedis("redis").WithDataVolume()` в AppHost; `catalog-api` і `aggregator` — `WithReference(redis).WaitFor(redis)` |
| L1 | `AddMemoryCache(SizeLimit = 1000)`, кожен запис `Size = 1`, sliding + absolute expiration |
| L2 | `AddRedisDistributedCache("redis")`, System.Text.Json, префікс `catalog:`, TTL за типом даних (`CacheTtl`) |
| Cache-aside | `TwoLevelCache.GetOrCreateAsync`: L1 → L2 → БД, рівні заповнюються на зворотному шляху; ключі `catalog:product:{id}`, `catalog:products:list:v{n}:{hash}` |
| Інвалідація | після POST/PUT/DELETE/зміни запасу — видалення `catalog:product:{id}` і інкремент версії списків; причина логується |
| Метрики кешу | Meter `Catalog.Cache`: `cache.hits` / `cache.misses` з тегом `cache.level` = L1/L2 |
| gRPC-контракти | `Services/*/*.Api/Protos/*.proto` (proto3, package, csharp_namespace); `<Protobuf GrpcServices="Server">` у сервісах, `"Client"` в Aggregator |
| gRPC-сервіси | `Grpc/*GrpcService.cs` — адаптери над BLL/MediatR; `NotFound` / `InvalidArgument` замість `Internal` |
| Interceptor | `Platform.ServiceDefaults/Grpc/ServerLoggingInterceptor.cs`: метод, peer, статус, тривалість |
| Aggregator v2 | `AddGrpcClient` для кожного сервісу (`https://catalog-api`), `Task.WhenAll` з дедлайнами, `Unavailable/DeadlineExceeded` → warning, кеш агрегату в Redis на 45 с (часткові відповіді не кешуються) |

```bash
curl http://localhost:5000/api/aggregator/v2/products/3          # перший виклик — gRPC, другий — з Redis
curl http://localhost:5000/api/aggregator/v2/orders/1/details
```

## Резильєнтність, бізнес-метрики та Health Checks (Лаб. 07)

### Політики стійкості клієнтів Aggregator

Усі `HttpClient` (і gRPC-клієнти, бо їх створює та сама фабрика) отримують **Standard Resilience Handler** через
`ConfigureHttpClientDefaults` у `Platform.ServiceDefaults`. Клієнти з іншими вимогами спершу викликають
`RemoveAllResilienceHandlers()`, а потім додають власний конвеєр (`Platform.Aggregator/Resilience/ResiliencePolicies.cs`) —
обробники не нашаровуються.

| Клієнт | Критичність | Retry | Circuit breaker | Таймаути | Обґрунтування |
|---|---|---|---|---|---|
| `CatalogClient`, `CatalogService` (gRPC) | **критичний** | 5 повторів, експоненційно від 0.5 с, jitter, лише безпечні методи | FailureRatio **0.3**, MinimumThroughput 10, SamplingDuration 30 с, BreakDuration 15 с, лог OnOpened/OnClosed/OnHalfOpened | спроба 3 с, загальний 30 с | без товарів картку не зібрати — варто зачекати довше; чутливіший breaker швидше захищає Каталог від лавини запитів; 5 × 3 с + затримки (~15.5 с) вкладаються в 30 с |
| `ReviewsClient`, `ReviewsService` (gRPC) | некритичний | 1 повтор, 0.2 с, jitter | FailureRatio **0.7**, MinimumThroughput 10, SamplingDuration 10 с, BreakDuration 10 с | спроба 2 с, загальний 5 с | без відгуків Aggregator повертає часткову відповідь — краще швидко здатися й показати warning |
| `OrdersClient`, `OrdersService` (gRPC) | стандартний | Standard Handler: 3 повтори, від 2 с, jitter | 10 % при ≥ 100 запитів / 30 с, 5 с | спроба 10 с, загальний 30 с | замовлення — обов'язкова частина «картки замовлення», стандартних налаштувань достатньо |

* **gRPC-статуси**: `Unavailable`/`DeadlineExceeded` приходять у трейлерах з HTTP 200, тож HTTP-обробник їх не повторює.
  Для них — `ServiceConfig` з `RetryPolicy` каналу (`WithGrpcRetry`): Catalog 4 спроби, Orders 3, Reviews 2;
  `RetryableStatusCodes = Unavailable, ResourceExhausted`.
* **Write-операції** не повторюються (`DisableForUnsafeHttpMethods`), щоб не створити замовлення двічі.
* Телеметрія resilience — Meter `Polly` (`resilience.polly.strategy.events` з тегом `event.name`: OnRetry, OnCircuitOpened…).

### Graceful degradation Redis

`Catalog.Bll/Caching/TwoLevelCache.cs` і `Platform.Aggregator/Services/GrpcAggregationService.cs`: кожна операція з Redis у
`try/catch (RedisConnectionException | RedisTimeoutException | RedisException)` — збій логується Warning, запит не падає,
дані читаються з БД / сервісів. Health check Redis має статус **Degraded**, а не Unhealthy.

### Бізнес-метрики (Meter API, `IMeterFactory`, Singleton)

| Meter | Інструменти | Теги (низька кардинальність) |
|---|---|---|
| `Orders.Api` (`Orders.Api/Metrics/OrderMetrics.cs`) | `orders.created` (Counter), `orders.status_changed` (Counter), `orders.operation.duration` (Histogram, ms), `orders.awaiting_confirmation`, `orders.awaiting_shipment` (ObservableGauge) | `order.status`, `order.status.from/to`, `operation`, `status` |
| `Catalog.Api` (`Catalog.Api/Metrics/CatalogMetrics.cs`) | `products.created`, `products.price_changed`, `products.stock_moved`, `catalog.operation.duration`, `products.low_stock` (Gauge) | `product.category`, `direction`, `operation`, `status` |
| `Reviews.Api` (`Reviews.WebApi/Metrics/ReviewMetrics.cs`) | `reviews.created`, `reviews.operation.duration`, `reviews.average_rating` (Gauge) | `review.type`, `review.rating` (1..5) |
| `Catalog.Cache` | `cache.hits`, `cache.misses`, `cache.invalidations` | `cache.level` (L1/L2), `cache.entity` |
| `Platform.Aggregator` | `aggregator.requests`, `aggregator.duration` | `scenario`, `result` |

Тривалість записується і для успішних, і для невдалих операцій. Ідентифікатори користувачів/замовлень у теги не потрапляють.
Нормальні діапазони: `orders.operation.duration` p95 < 300 мс; частка `cache.misses{L2}` < 30 %; `orders.awaiting_confirmation` < 50.

### Health checks

| Сервіс | Перевірки (`/health/ready`) |
|---|---|
| orders-api | `orders-db` (SQL Server, `SELECT 1`, 5 с) |
| catalog-api | `catalog-db` (SQL Server), `redis-cache` (PING, 3 с, **Degraded**) |
| reviews-api | `reviews-db` (MongoDB ping, 5 с) |
| aggregator | `catalog-api`, `orders-api`, `reviews-api` (gRPC Health Checking Protocol, дедлайн 2 с; Reviews → Degraded), `redis-cache` (Degraded) |

* `/health/live` — лише тег `live` (процес живий, без зовнішніх залежностей); `/health/ready` — тег `ready`, JSON з деталями
  кожної перевірки. Обидва endpoint-и зареєстровані **в усіх середовищах** (потрібні пробам Kubernetes, Лаб. 10).
* Кожен сервіс публікує `AddGrpcHealthChecks()` + `MapGrpcHealthChecksService()`.
* AppHost: `WithHttpHealthCheck("/health/ready")` — Dashboard показує стан ресурсів, `WaitFor` чекає здорову залежність.

```bash
curl http://localhost:5000/api/aggregator/v2/products/3     # зупиніть reviews-api в Dashboard → 200 + warnings
curl https://localhost:<catalog-port>/health/ready         # зупиніть Redis → "Degraded"
curl https://localhost:<orders-port>/health/live           # зупиніть SQL Server → live 200, ready 503
```

## Подієва архітектура з RabbitMQ (Лаб. 08)

```
Platform.EventBus/
├── Abstractions/   IntegrationEvent (EventId, OccurredAt, CorrelationId), IEventPublisher, IIntegrationEventHandler<T>
├── Events/         ProductUpdated, ProductPriceChanged, OrderCreated — незмінні record-и, назви в минулому часі
├── RabbitMq/       топологія, publisher, consumer (BackgroundService), телеметрія, опції
└── EventBusExtensions.cs   AddEventBus() → AddRabbitMQClient("messaging"); AddEventConsumer(queue).Subscribe<TEvent, THandler>()
```

| Потік | Publisher → routing key | Consumer (черга) | Дія |
|---|---|---|---|
| Зміна товару | Catalog → `catalog.product.updated` | `orders-api.events` | upsert локального довідника `dbo.Products` (ціни в оформлених позиціях — знімок, не змінюються) |
| Зміна товару | Catalog → `catalog.product.updated` | `reviews-api.events` | оновлення `product.productName` в усіх відгуках і обговореннях товару |
| Нове замовлення | Orders → `orders.order.created` | `catalog-api.events` | резервування запасу (`RemoveStock`) |
| Зміна ціни | Catalog → `catalog.product.price-changed` | — (для аналітики) | — |

* **Топологія при старті**: durable topic exchange `bookhub.events`; на кожен сервіс-споживач — durable черга з
  `x-dead-letter-exchange = bookhub.events.dlx`; DLQ `<queue>.dlq` прив'язана до DLX.
* **Публікація**: camelCase JSON; `BasicProperties`: `Persistent`, `ContentType`, `MessageId = EventId`, `Type`, заголовки
  `x-correlation-id` і W3C `traceparent`. Події публікуються **лише після успішного** `SaveChanges`/`Commit`
  (`ProductService`, `OrderService`); збій брокера логується з бізнес-контекстом (наступний крок — Transactional Outbox).
* **Споживання**: `RabbitMqConsumerService` — `BasicQos(prefetch 10)`, `AsyncEventingBasicConsumer`, `BasicConsumeAsync(autoAck: false)`,
  коректна зупинка за `stoppingToken`; окремий обробник на тип події в DI-scope повідомлення.
* **Ack/Nack**: успіх → Ack; некоректний JSON, невідомий тип, `PermanentEventException` → `Nack(requeue: false)` → DLQ.
* **Повтори**: тимчасова помилка → перепублікація в ту ж чергу з `x-retry-count + 1` і Ack оригіналу; після 3 спроб → DLQ.
* **Ідемпотентність**: EventId з унікальним ключем у тій самій транзакції, що й бізнес-зміни —
  OrdersDb `dbo.ProcessedEvents` (транзакція UoW), CatalogDb `ProcessedEvents` (міграція `AddProcessedEvents`, один `SaveChangesAsync`),
  ReviewsDb колекція `processedEvents` (`_id = EventId`).
* **Трасування**: `traceparent` впроваджується при публікації, у споживачі — `Activity` типу Consumer з тегами `messaging.*`
  (ActivitySource `BookHub.EventBus` підключено в ServiceDefaults). Логи обробки містять EventId, тип події, Id сутностей і CorrelationId.

RabbitMQ Management UI — посилання на ресурс `messaging` в Aspire Dashboard.

```bash
# змінити ціну → ProductUpdated → довідник Замовлень і назви у Відгуках оновлюються
curl -X PUT http://localhost:5000/api/catalog/products/3 -H "Content-Type: application/json" \
     -d '{"name":"Clean Code (2nd ed.)","author":"Robert C. Martin","price":1390,"publisherId":3}'
curl http://localhost:5000/api/orders/products          # нова назва/ціна в довіднику
curl "http://localhost:5000/api/reviews?productId=3"    # productName оновлено
```

## Аутентифікація та авторизація (Лаб. 09)

### Секрети AppHost

```bash
dotnet user-secrets set "Parameters:jwt-signing-key" "<random 64 chars>" --project Platform.AppHost
dotnet user-secrets set "Parameters:aggregator-client-secret" "<secret>" --project Platform.AppHost
dotnet user-secrets set "Parameters:seed-user-password" "<Passw0rd!>" --project Platform.AppHost
dotnet user-secrets set "Parameters:keycloak-admin-password" "<admin password>" --project Platform.AppHost
```

### Частина 1 — Auth Service (ASP.NET Identity + JWT) — `Services/Auth/Auth.Api`

| Вимога | Реалізація |
|---|---|
| Окрема база | `IdentityDb` в AppHost; `AuthDbContext : IdentityDbContext<ApplicationUser>`; міграція `Migrations/InitialIdentity` |
| Identity | `AddIdentity`: пароль ≥ 8 (цифра, великі/малі літери, спецсимвол), lockout 5 спроб / 15 хв, `RequireConfirmedEmail`, унікальний email |
| JWT | access 15 хв (claims `sub`, `username`, `email`, `role`), refresh 7 днів — у БД лише SHA-256 хеш, `ExpiresAt`, `RevokedAt`; ротація: старий токен відкликається і посилається на новий; повторне використання → відкликання всіх сесій; refresh у **HttpOnly** cookie (`Secure`, `SameSite=Strict`, `Path=/auth`) |
| Ендпоінти | `POST /auth/register` (роль User), `/auth/login` (access у тілі, refresh у cookie), `/auth/refresh-token`, `/auth/revoke-token`, `/auth/logout` |
| Email/пароль | `/auth/verify-email`, `/auth/resend-verification` (login без підтвердження → 403), `/auth/forgot-password`, `/auth/reset-password` (одноразовий токен Identity + відкликання refresh-токенів), `/auth/change-password` |
| Ролі | `User`, `Admin` створюються при старті; `/users` — `[Authorize(Roles = "Admin")]`, `/users/me`, `/users/{id}` — власник (userId з токена) або Admin |
| Gateway | маршрути `/auth/**` (anonymous) і `/users/**` (authenticated) → `auth-api`; Gateway валідує JWT до проксіювання |
| Мікросервіси | `AddJwtBearer` + `[Authorize]`; claims у бізнес-логіці: `CreatedBy/ChangedBy` замовлень, автор відгуку (`sub`, `name`) |

### Частина 2 — Duende IdentityServer — `Platform.IdentityServer`

* `AddIdentityServer().AddConfigurationStore().AddOperationalStore().AddAspNetIdentity<ApplicationUser>()`, окрема база
  `IdentityServerDb`, міграції трьох контекстів (`Migrations/Identity`, `Migrations/Configuration`, `Migrations/PersistedGrant`),
  сторінки входу/виходу (Quickstart UI, Razor Pages).
* ApiScopes `orders.read/write`, `catalog.read/write`, `reviews.read/write`, `payments.read/write`; ApiResources
  `orders-api`, `catalog-api`, `reviews-api`, `payments-api`.
* Клієнти: **postman** — Authorization Code (+PKCE, секрет); **aggregator** — Client Credentials; **swagger** — public client з PKCE без секрету.
* Сервіси: `AddJwtBearer("IdentityServer")` з `Authority` (service discovery) і `Audience` = ApiResource; політики за scope
  (`[Authorize(Policy = "orders.read")]` на gRPC-сервісах). Aggregator отримує токен Client Credentials
  (`Platform.Aggregator/Auth/ClientCredentialsTokenHandler.cs`) і додає його до HTTP- та gRPC-викликів.

### Частина 3 — Keycloak + Pulumi

* AppHost: `AddKeycloak("keycloak", 8080).WithDataVolume()` (траєкторія А); сервіси отримують `WithReference(keycloak)` і в
  `Program.cs` налаштовують `AddKeycloakJwtBearer("keycloak", realm: "bookhub")` з audience свого resource server-а.
* `infra/keycloak` — Pulumi (C#): realm, resource servers `orders-api` / `products-api` / `payments-api` з повним набором scopes,
  ролі `admin / manager / user / guest` за матрицею (платежі — лише admin), клієнти через extension-методи, тестові
  користувачі, mapper `authorization.permissions`, outputs. Деталі — [infra/keycloak/README.md](infra/keycloak/README.md).
* **Permission-based авторизація**: `Platform.ServiceDefaults/Authorization` — `[RequirePermission("orders:read")]`,
  `PermissionRequirement`, `PermissionAuthorizationHandler` (читає `authorization.permissions`, scopes IdentityServer, ролі за
  матрицею), `PermissionPolicyProvider`. Атрибут стоїть на **кожному** ендпоінті Orders, Products (Catalog) і Payments.

| Роль | orders | products | payments |
|---|---|---|---|
| admin | read, create, update, delete | read, create, update, delete | read, create, refund |
| manager | read, create, update, delete | read, create, update, delete | — |
| user | read, create | read | — |
| guest | — | read | — |

```bash
# Auth Service
curl -X POST http://localhost:5000/auth/register -H "Content-Type: application/json" \
     -d '{"email":"reader@example.com","userName":"reader","fullName":"Reader","password":"Passw0rd!"}'
curl -X POST http://localhost:5000/auth/login -c cookies.txt -H "Content-Type: application/json" \
     -d '{"userNameOrEmail":"admin","password":"<seed-user-password>"}'
curl -X POST http://localhost:5000/auth/refresh-token -b cookies.txt -c cookies.txt
curl http://localhost:5000/api/orders/1 -H "Authorization: Bearer <access_token>"
# IdentityServer: токен Aggregator
curl -X POST https://localhost:<identityserver-port>/connect/token \
     -d grant_type=client_credentials -d client_id=aggregator -d client_secret=<secret> -d scope="orders.read catalog.read"
```

## Розгортання в Kubernetes (Лаб. 10)

### Aspir8 (aspirate) — маніфести всієї платформи

`Platform.AppHost/aspirate-output/` — згенеровано `aspirate` з Aspire-маніфесту AppHost (kustomize): Deployments, Services і
ConfigMaps (`configMapGenerator`) для всіх сервісів, Gateway, Aggregator, IdentityServer і StatefulSets/Services інфраструктури
(SQL Server, MongoDB, Redis, RabbitMQ, Keycloak). Секрети з вихідних ConfigMap-ів прибрано — вони підключаються через
`envFrom.secretRef: <ім'я>-secrets`, команди створення — у [aspirate-output/README.md](Platform.AppHost/aspirate-output/README.md).

### Ручне розгортання — `deploy/k8s/`

| Вимога | Реалізація |
|---|---|
| Багатоетапні Dockerfile | `Services/Catalog/Catalog.Api/Dockerfile`, `Platform.ApiGateway/Dockerfile`: збірка в `sdk:10.0`, спершу копіюються `.csproj` і виконується `dotnet restore` окремим шаром, фінальний образ `aspnet:10.0` |
| Deployment | `*-deployment.yaml`: `replicas: 2`, `imagePullPolicy: IfNotPresent`, `containerPort: 8080`, `livenessProbe /health/live`, `readinessProbe /health/ready` |
| Service | `*-service.yaml`: `ClusterIP`, selector за лейблом поду, `port 80 → targetPort 8080` — стабільне DNS-ім'я |
| ConfigMap | `configmap.yaml`: `services__<ім'я>__http__0` з DNS-іменами Service-ів; підключення через `envFrom.configMapRef` |
| Secrets | поза репозиторієм: `kubectl create secret generic catalog-api-secrets --from-literal=...` ([deploy/k8s/README.md](deploy/k8s/README.md)), підключення `envFrom.secretRef` |
| Health endpoints | `/health/live` і `/health/ready` реєструються `MapDefaultEndpoints()` **в усіх середовищах** (не лише Development) |

```bash
docker build -f Services/Catalog/Catalog.Api/Dockerfile -t bookhub/catalog-api:1.0 .
docker build -f Platform.ApiGateway/Dockerfile -t bookhub/gateway:1.0 .
kubectl apply -f deploy/k8s/configmap.yaml
kubectl apply -f deploy/k8s/
kubectl port-forward svc/gateway 5000:80
```

## Сервіс Замовлень (Лаб. 02 — ADO.NET + Dapper)

```
Services/Orders/
├── Orders.Api      контролери, глобальний обробник помилок (ProblemDetails), Program.cs, Swagger
├── Orders.Bll      DTO + DataAnnotations, AutoMapper-профіль, сервіси (бізнес-правила, транзакції)
├── Orders.Dal      IGenericRepository<T>, BaseDapperRepository<T>, Dapper/ADO.NET репозиторії, Unit of Work
└── Orders.Domain   моделі, доменні винятки (NotFound / BusinessConflict / Validation)
```

Залежності лише в один бік: `Api → Bll → Dal → Domain`.

| Компонент | Реалізація |
|---|---|
| `IGenericRepository<T>` | спільний CRUD; `IOrderRepository`, `ICustomerRepository` успадковують його й додають специфічні запити |
| `BaseDapperRepository<T>` | CRUD написано один раз (SELECT/INSERT/UPDATE/м'яке видалення) |
| `OrderRepository` (Dapper) | multi-mapping `Order + OrderItem`, виклик процедур `usp_Order_ChangeStatus`, `usp_Order_SoftDelete` |
| `CustomerRepository` (Dapper) | multi-mapping 1:1 `Customer + CustomerProfile`, процедура `usp_Customer_GetOrderStats` |
| `ProductDirectoryRepository` (чистий ADO.NET) | `SqlCommand` + параметри + ручне читання `SqlDataReader`, upsert довідника |
| `UnitOfWork` | одне `SqlConnection` на запит, `BeginTransactionAsync(IsolationLevel)`, `CommitAsync`, `RollbackAsync`, `DisposeAsync` |
| `OrderService.CreateAsync` | транзакція через 3 репозиторії (Orders, OrderItems, OrderStatusHistory), знімок ціни/назви, відкат при помилці |
| `GlobalExceptionHandler` | NotFound → 404, BusinessConflict → 409, Validation → 400, інше → 500 |

### Приклади запитів

```bash
# Створити клієнта
curl -X POST https://localhost:7101/api/orders/customers -H "Content-Type: application/json" \
     -d '{"email":"new.client@example.com","fullName":"Новий Клієнт","phone":"+380501234567"}'

# Створити замовлення (201 + Location)
curl -X POST https://localhost:7101/api/orders -H "Content-Type: application/json" \
     -d '{"customerId":1,"items":[{"productId":1,"quantity":2},{"productId":3,"quantity":1}],"comment":"Доставка Новою поштою"}'

# Отримати замовлення з позиціями
curl https://localhost:7101/api/orders/1

# Змінити статус (недопустимий перехід → 409 ProblemDetails)
curl -X PATCH https://localhost:7101/api/orders/1/status -H "Content-Type: application/json" \
     -d '{"status":"Confirmed","reason":"Оплата очікується"}'

# Історія статусів, замовлення клієнта, статистика
curl https://localhost:7101/api/orders/1/history
curl "https://localhost:7101/api/orders?customerId=1"
curl https://localhost:7101/api/orders/customers/1/stats

# М'яке видалення (204)
curl -X DELETE https://localhost:7101/api/orders/2
```

Swagger UI: `https://localhost:7101/swagger`.

## Сервіс Каталогу (Лаб. 03 — EF Core Code First)

```
Services/Catalog/
├── Catalog.Api      контролери, GlobalExceptionHandler, Serilog, Program.cs
├── Catalog.Bll      DTO, FluentValidation-валідатори, специфікації, AutoMapper, сервіси
├── Catalog.Dal      CatalogDbContext, Configurations/, Migrations/, репозиторії, Unit of Work, Seeding/
└── Catalog.Domain   сутності з інкапсульованими правилами (Product.AddStock/RemoveStock/ChangePrice)
```

| Вимога | Реалізація |
|---|---|
| Fluent API | окремі `IEntityTypeConfiguration<T>`, `ApplyConfigurationsFromAssembly`; `decimal(18,2)`, унікальні індекси, CHECK |
| Зв'язки | 1:N `Publisher → Product`, 1:1 `Product → ProductDetail` (FK = PK), M:N через `ProductCategory` (складений ключ + `AddedAt`, `IsPrimary`) |
| Міграції | `Catalog.Dal/Migrations/*_InitialCreate.cs` + знімок моделі + `InitialCreate.sql` (ідемпотентний скрипт) |
| Репозиторії | `GenericRepository<T>` на `DbSet`; `ProductRepository` — Eager (`Include/ThenInclude`), `PublisherRepository` — Explicit (`Entry().Collection().LoadAsync`), `CategoryRepository` — LINQ M:N за `AddedAt` |
| Unit of Work | репозиторії лише змінюють стан, зберігає один `SaveChangesAsync` |
| Сідінг | `HasData` для довідника категорій; `DbInitializer` після `MigrateAsync` з перевіркою наявності; вимикається `Catalog:SeedData=false` |
| Запити | `ProductFilterSpecification`: фільтри, сортування за білим списком полів, `Skip/Take` до `ToListAsync`; відповідь з `totalCount`, `totalPages`, `hasNext`, `hasPrevious` |
| Валідація | DataAnnotations на DTO + валідатори FluentValidation (`AddValidatorsFromAssembly`, явний `ValidateAndThrowAsync`) → `ValidationProblemDetails` |
| Логування | Serilog (Console sink, `FromLogContext`, `WithMachineName`), SQL-команди EF Core, `UseSerilogRequestLogging` |

```bash
# Міграції (з кореня репозиторію)
dotnet tool restore
dotnet ef migrations add <Name> --project Services/Catalog/Catalog.Dal --startup-project Services/Catalog/Catalog.Dal

# Приклади запитів
curl "https://localhost:7102/api/catalog/products?page=1&pageSize=5&categoryId=2&sortBy=price&sortDir=desc"
curl https://localhost:7102/api/catalog/products/1
curl -X POST https://localhost:7102/api/catalog/products -H "Content-Type: application/json" \
     -d '{"sku":"BK-0100","name":"Intermezzo","author":"Михайло Коцюбинський","price":210,"stockQuantity":15,"publisherId":1,"categoryIds":[2],"detail":{"isbn":"978-966-448-100-1","pages":64,"language":"uk","publishedYear":2022}}'
curl -X POST https://localhost:7102/api/catalog/products/1/stock/remove -H "Content-Type: application/json" -d '{"quantity":3}'
curl https://localhost:7102/api/catalog/publishers/1
curl "https://localhost:7102/api/catalog/categories/2/new-arrivals?days=30"
```

## Сервіс Відгуків (Лаб. 04 — Clean Architecture + CQRS + MongoDB)

```
Services/Reviews/
├── Reviews.Domain          BaseEntity (ObjectId, CreatedAt, UpdatedAt, Version), ValueObject, Review/Comment/Discussion,
│                           VO Rating/AuthorInfo/ProductRef, інтерфейси репозиторіїв (без MongoDB.Driver)
├── Reviews.Application     ICommand/ICommand<T>/IQuery<T>, Commands/, Queries/, Behaviors/, IReviewQueries, валідатори, AutoMapper
├── Reviews.Infrastructure  MongoDbContext (IOptions), BsonMappings (camelCase, приватні поля, VO через конструктор),
│                           репозиторії з оптимістичною конкурентністю, ReviewQueries (проєкції), індекси, сідінг
└── Reviews.WebApi          контролер лише через ISender, GlobalExceptionHandler, Program.cs
```

Залежності: `Domain → ∅`, `Application → Domain`, `Infrastructure → Application, Domain`, `WebApi → Application, Infrastructure`.

| Вимога | Реалізація |
|---|---|
| CQRS | команди `CreateReview`, `UpdateReview`, `DeleteReview`, `AddComment`, `MarkReviewHelpful`; запити `GetReviewById`, `GetReviewsList`, `GetProductRating` |
| Pipeline | `AddMediatR(RegisterServicesFromAssembly)` + `AddOpenBehavior`: Logging → Validation → Performance |
| Валідація | FluentValidation для команд і запитів; перевірка ObjectId; асинхронне правило «один відгук на товар від користувача» (запит до MongoDB) |
| Конкурентність | `ReplaceOne` з фільтром `{_id, version: expected}`; `MatchedCount == 0` → 409 |
| Читання | `IReviewQueries` → проєкція в DTO на стороні MongoDB, `$match` + `skip/limit` |
| Помилки | NotFound 404, Validation 400, Conflict 409, DuplicateKey 409, MongoConnectionException 503 |
| Індекси/сідінг | `MongoIndexInitializer` (складений, унікальний, текстовий), `IDataSeeder` з `InsertMany` і перевіркою наявності |

```bash
curl "https://localhost:7103/api/reviews?productId=3&minRating=4&page=1&pageSize=10&sortBy=rating"
curl -X POST https://localhost:7103/api/reviews -H "Content-Type: application/json" \
     -d '{"productId":2,"productName":"Тіні забутих предків","authorId":"u-olena","authorName":"Олена К.","rating":5,"type":"text","title":"Шедевр","body":"Перечитую щороку"}'
curl -X PUT https://localhost:7103/api/reviews/<id> -H "Content-Type: application/json" \
     -d '{"requestedBy":"u-olena","rating":4,"title":"Шедевр","body":"Оновлено","version":1}'
curl -X POST https://localhost:7103/api/reviews/<id>/comments -H "Content-Type: application/json" \
     -d '{"authorId":"u-taras","authorName":"Тарас","text":"Згоден"}'
curl https://localhost:7103/api/reviews/products/3/rating
```

## Структура репозиторію

```
README.md            опис домену, запуск, розгортання БД, таблиця дублювання
docs/                ERD-діаграми, опис контекстів, приклади документів
db/p1/               OrdersDb: schema.sql · procedures.sql · seed.sql
db/p2/               CatalogDb: schema.sql · seed.sql
db/p3/               ReviewsDb: collections.js · seed.js
Platform.slnx        одне рішення на всю платформу
Platform.AppHost/    оркестрація Aspire (AppHost.cs)
Services/Orders/     Проєкт №1 — ADO.NET + Dapper
Services/Catalog/    Проєкт №2 — EF Core
Services/Reviews/    Проєкт №3 — Clean Architecture + CQRS + MongoDB
Platform.ServiceDefaults/ Platform.ApiGateway/ Platform.Aggregator/ Platform.EventBus/ Platform.IdentityServer/
Services/Auth/Auth.Api/      ASP.NET Identity + JWT
Services/Payments/Payments.Api/  демо-сервіс для перевірки доступу
infra/keycloak/      Pulumi: realm, clients, ролі
deploy/k8s/          власні маніфести Kubernetes
Platform.AppHost/aspirate-output/  маніфести, згенеровані aspirate
```
