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
| `reviews.productName`, `discussions.productName` | ReviewsDb | `Products.Name` (CatalogDb) | копія, оновлюється подією `ProductUpdated` (Лаб. 08) у всіх документах товару |
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
```
