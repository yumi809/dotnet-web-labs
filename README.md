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
```
