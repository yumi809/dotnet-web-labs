# ERD-діаграми

Суцільні лінії — FOREIGN KEY усередині однієї бази. Між базами зв'язків FK немає:
`OrderItems.ProductId`, `Products.ProductId` (OrdersDb) і `reviews.productId` (ReviewsDb) — логічні посилання на `CatalogDb.Products.Id`.

## Проєкт №1 — OrdersDb (SQL Server, ADO.NET + Dapper)

```mermaid
erDiagram
    Customers ||--o| CustomerProfiles : "1:1 (PK=FK)"
    Customers ||--o{ Orders : "1:N"
    Orders ||--|{ OrderItems : "1:N"
    Orders ||--o{ OrderStatusHistory : "1:N"
    Orders ||--o{ Payments : "1:N"
    Orders ||--o{ OrderPromotions : "M:N"
    Promotions ||--o{ OrderPromotions : "M:N"

    Customers { bigint Id PK
        nvarchar Email UK
        nvarchar FullName
        datetime2 CreatedAt
        nvarchar CreatedBy
        datetime2 UpdatedAt
        nvarchar UpdatedBy
        bit IsDeleted
        rowversion RowVersion }
    CustomerProfiles { bigint CustomerId PK, FK
        nvarchar ShippingCity
        nvarchar ShippingAddress
        nvarchar LoyaltyLevel }
    Orders { bigint Id PK
        nvarchar OrderNumber UK
        bigint CustomerId FK
        nvarchar Status
        decimal TotalAmount
        decimal DiscountAmount
        bit IsDeleted
        rowversion RowVersion }
    OrderItems { bigint Id PK
        bigint OrderId FK
        bigint ProductId "logical ref to CatalogDb"
        nvarchar ProductName "snapshot"
        decimal UnitPrice "snapshot"
        int Quantity }
    OrderStatusHistory { bigint Id PK
        bigint OrderId FK
        nvarchar FromStatus
        nvarchar ToStatus
        datetime2 ChangedAt }
    Promotions { int Id PK
        nvarchar Code UK
        decimal DiscountPercent }
    OrderPromotions { bigint OrderId PK, FK
        int PromotionId PK, FK
        datetime2 AppliedAt
        decimal DiscountAmount }
    Payments { bigint Id PK
        bigint OrderId FK
        decimal Amount
        nvarchar Method
        nvarchar Status }
    Products { bigint ProductId PK "copy of CatalogDb"
        nvarchar Name
        decimal Price }
```

## Проєкт №2 — CatalogDb (SQL Server, EF Core)

```mermaid
erDiagram
    Publishers ||--o{ Products : "1:N"
    Products ||--|| ProductDetails : "1:1 (PK=FK)"
    Products ||--o{ ProductCategories : "M:N"
    Categories ||--o{ ProductCategories : "M:N"

    Publishers { int Id PK
        nvarchar Name UK
        nvarchar Country }
    Products { bigint Id PK
        nvarchar Sku UK
        nvarchar Name
        nvarchar Author
        decimal Price
        int StockQuantity
        int PublisherId FK
        rowversion RowVersion }
    ProductDetails { bigint ProductId PK, FK
        nvarchar Isbn UK
        int Pages
        nvarchar Language
        int PublishedYear }
    Categories { int Id PK
        nvarchar Name
        nvarchar Slug UK }
    ProductCategories { bigint ProductId PK, FK
        int CategoryId PK, FK
        datetime2 AddedAt "own field"
        bit IsPrimary }
```

## Проєкт №3 — ReviewsDb (MongoDB)

```mermaid
erDiagram
    reviews ||--o{ comments : "embedded array"
    reviews ||--|| author : "embedded VO"
    discussions ||--o{ recentMessages : "embedded (last 3)"
    discussions ||--o{ discussionMessages : "reference discussionId"
    productRatings ||..o{ reviews : "materialized summary by productId"
```

Приклад документа `reviews` (тип `pros-cons`):

```json
{
  "_id": { "$oid": "6650a1f2c1e4b2a1d0f00001" },
  "productId": 3,
  "productName": "Clean Code",
  "type": "pros-cons",
  "author": { "userId": "u-taras", "displayName": "Тарас Б." },
  "rating": { "value": 4 },
  "title": "Класика для розробника",
  "pros": ["Практичні приклади"],
  "cons": ["Приклади на Java"],
  "comments": [],
  "helpfulVotes": 0,
  "createdAt": "2026-11-20T10:00:00Z",
  "version": 1
}
```
