/* =====================================================================
   Проєкт №1 — OrdersDb (контекст «Замовлення»)
   Технологія доступу: ADO.NET + Dapper (Лаб. 02)
   Скрипт ідемпотентний: повторний запуск нічого не ламає.
   ===================================================================== */
IF DB_ID(N'OrdersDb') IS NULL
    CREATE DATABASE OrdersDb;
GO
USE OrdersDb;
GO

/* ---------- Customers (корінь 1:N для Orders, 1:1 для CustomerProfiles) ---------- */
IF OBJECT_ID(N'dbo.Customers', N'U') IS NULL
CREATE TABLE dbo.Customers
(
    Id          BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Customers PRIMARY KEY,
    Email       NVARCHAR(256)  NOT NULL,
    FullName    NVARCHAR(200)  NOT NULL,
    Phone       NVARCHAR(32)   NULL,
    -- аудит
    CreatedAt   DATETIME2(3)   NOT NULL CONSTRAINT DF_Customers_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy   NVARCHAR(100)  NOT NULL CONSTRAINT DF_Customers_CreatedBy DEFAULT N'system',
    UpdatedAt   DATETIME2(3)   NULL,
    UpdatedBy   NVARCHAR(100)  NULL,
    IsDeleted   BIT            NOT NULL CONSTRAINT DF_Customers_IsDeleted DEFAULT 0,
    RowVersion  ROWVERSION     NOT NULL,
    CONSTRAINT UQ_Customers_Email UNIQUE (Email),
    CONSTRAINT CK_Customers_Email CHECK (Email LIKE N'%_@_%._%')
);
GO

/* ---------- CustomerProfiles: зв'язок 1:1 (FK = PK залежної таблиці) ---------- */
IF OBJECT_ID(N'dbo.CustomerProfiles', N'U') IS NULL
CREATE TABLE dbo.CustomerProfiles
(
    CustomerId       BIGINT        NOT NULL CONSTRAINT PK_CustomerProfiles PRIMARY KEY,
    ShippingCity     NVARCHAR(100) NOT NULL,
    ShippingAddress  NVARCHAR(300) NOT NULL,
    PostalCode       NVARCHAR(10)  NULL,
    LoyaltyLevel     NVARCHAR(20)  NOT NULL CONSTRAINT DF_CustomerProfiles_Loyalty DEFAULT N'Bronze',
    UpdatedAt        DATETIME2(3)  NULL,
    CONSTRAINT FK_CustomerProfiles_Customers FOREIGN KEY (CustomerId)
        REFERENCES dbo.Customers (Id) ON DELETE CASCADE,          -- профіль не існує без клієнта
    CONSTRAINT CK_CustomerProfiles_Loyalty CHECK (LoyaltyLevel IN (N'Bronze', N'Silver', N'Gold'))
);
GO

/* ---------- Products: локальний довідник-копія з Каталогу (оновлюється подіями, Лаб. 08) ---------- */
IF OBJECT_ID(N'dbo.Products', N'U') IS NULL
CREATE TABLE dbo.Products
(
    ProductId    BIGINT        NOT NULL CONSTRAINT PK_Products PRIMARY KEY,  -- Id з CatalogDb, НЕ FK
    Name         NVARCHAR(200) NOT NULL,
    Price        DECIMAL(18,2) NOT NULL,
    IsAvailable  BIT           NOT NULL CONSTRAINT DF_Products_IsAvailable DEFAULT 1,
    SyncedAt     DATETIME2(3)  NOT NULL CONSTRAINT DF_Products_SyncedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT CK_Products_Price CHECK (Price >= 0)
);
GO

/* ---------- Orders: корінь агрегату «Замовлення» ---------- */
IF OBJECT_ID(N'dbo.Orders', N'U') IS NULL
CREATE TABLE dbo.Orders
(
    Id            BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Orders PRIMARY KEY,
    OrderNumber   NVARCHAR(20)   NOT NULL,
    CustomerId    BIGINT         NOT NULL,
    Status        NVARCHAR(20)   NOT NULL CONSTRAINT DF_Orders_Status DEFAULT N'New',
    TotalAmount   DECIMAL(18,2)  NOT NULL CONSTRAINT DF_Orders_Total DEFAULT 0,
    DiscountAmount DECIMAL(18,2) NOT NULL CONSTRAINT DF_Orders_Discount DEFAULT 0,
    Comment       NVARCHAR(500)  NULL,
    CreatedAt     DATETIME2(3)   NOT NULL CONSTRAINT DF_Orders_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy     NVARCHAR(100)  NOT NULL CONSTRAINT DF_Orders_CreatedBy DEFAULT N'system',
    UpdatedAt     DATETIME2(3)   NULL,
    UpdatedBy     NVARCHAR(100)  NULL,
    IsDeleted     BIT            NOT NULL CONSTRAINT DF_Orders_IsDeleted DEFAULT 0,
    RowVersion    ROWVERSION     NOT NULL,                       -- оптимістична конкурентність
    CONSTRAINT UQ_Orders_OrderNumber UNIQUE (OrderNumber),
    CONSTRAINT FK_Orders_Customers FOREIGN KEY (CustomerId)
        REFERENCES dbo.Customers (Id) ON DELETE NO ACTION,       -- історію замовлень не видаляємо каскадом
    CONSTRAINT CK_Orders_Status CHECK (Status IN (N'New', N'Confirmed', N'Paid', N'Shipped', N'Completed', N'Cancelled')),
    CONSTRAINT CK_Orders_Amounts CHECK (TotalAmount >= 0 AND DiscountAmount >= 0 AND DiscountAmount <= TotalAmount)
);
GO

/* ---------- OrderItems: 1:N від Orders; ProductName/UnitPrice — знімок на момент покупки ---------- */
IF OBJECT_ID(N'dbo.OrderItems', N'U') IS NULL
CREATE TABLE dbo.OrderItems
(
    Id           BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_OrderItems PRIMARY KEY,
    OrderId      BIGINT         NOT NULL,
    ProductId    BIGINT         NOT NULL,      -- логічне посилання на CatalogDb.Products (без FK між базами)
    ProductName  NVARCHAR(200)  NOT NULL,      -- знімок
    UnitPrice    DECIMAL(18,2)  NOT NULL,      -- знімок
    Quantity     INT            NOT NULL,
    LineTotal    AS (UnitPrice * Quantity) PERSISTED,
    CONSTRAINT FK_OrderItems_Orders FOREIGN KEY (OrderId)
        REFERENCES dbo.Orders (Id) ON DELETE CASCADE,            -- позиції живуть разом із замовленням
    CONSTRAINT CK_OrderItems_Quantity CHECK (Quantity > 0),
    CONSTRAINT CK_OrderItems_UnitPrice CHECK (UnitPrice >= 0),
    CONSTRAINT UQ_OrderItems_Order_Product UNIQUE (OrderId, ProductId)
);
GO

/* ---------- OrderStatusHistory: 1:N, журнал переходів статусу ---------- */
IF OBJECT_ID(N'dbo.OrderStatusHistory', N'U') IS NULL
CREATE TABLE dbo.OrderStatusHistory
(
    Id          BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_OrderStatusHistory PRIMARY KEY,
    OrderId     BIGINT        NOT NULL,
    FromStatus  NVARCHAR(20)  NULL,
    ToStatus    NVARCHAR(20)  NOT NULL,
    ChangedAt   DATETIME2(3)  NOT NULL CONSTRAINT DF_OSH_ChangedAt DEFAULT SYSUTCDATETIME(),
    ChangedBy   NVARCHAR(100) NOT NULL CONSTRAINT DF_OSH_ChangedBy DEFAULT N'system',
    Reason      NVARCHAR(300) NULL,
    CONSTRAINT FK_OSH_Orders FOREIGN KEY (OrderId) REFERENCES dbo.Orders (Id) ON DELETE CASCADE
);
GO

/* ---------- Promotions + OrderPromotions: зв'язок M:N з власним полем ---------- */
IF OBJECT_ID(N'dbo.Promotions', N'U') IS NULL
CREATE TABLE dbo.Promotions
(
    Id              INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Promotions PRIMARY KEY,
    Code            NVARCHAR(30)  NOT NULL,
    Description     NVARCHAR(200) NULL,
    DiscountPercent DECIMAL(5,2)  NOT NULL,
    ValidFrom       DATE          NOT NULL,
    ValidTo         DATE          NOT NULL,
    IsActive        BIT           NOT NULL CONSTRAINT DF_Promotions_IsActive DEFAULT 1,
    CONSTRAINT UQ_Promotions_Code UNIQUE (Code),
    CONSTRAINT CK_Promotions_Percent CHECK (DiscountPercent > 0 AND DiscountPercent <= 50),
    CONSTRAINT CK_Promotions_Dates CHECK (ValidTo >= ValidFrom)
);
GO

IF OBJECT_ID(N'dbo.OrderPromotions', N'U') IS NULL
CREATE TABLE dbo.OrderPromotions
(
    OrderId        BIGINT        NOT NULL,
    PromotionId    INT           NOT NULL,
    AppliedAt      DATETIME2(3)  NOT NULL CONSTRAINT DF_OrderPromotions_AppliedAt DEFAULT SYSUTCDATETIME(),
    DiscountAmount DECIMAL(18,2) NOT NULL,
    CONSTRAINT PK_OrderPromotions PRIMARY KEY (OrderId, PromotionId),
    CONSTRAINT FK_OrderPromotions_Orders FOREIGN KEY (OrderId) REFERENCES dbo.Orders (Id) ON DELETE CASCADE,
    CONSTRAINT FK_OrderPromotions_Promotions FOREIGN KEY (PromotionId) REFERENCES dbo.Promotions (Id),
    CONSTRAINT CK_OrderPromotions_Amount CHECK (DiscountAmount >= 0)
);
GO

/* ---------- Payments: 1:N від Orders ---------- */
IF OBJECT_ID(N'dbo.Payments', N'U') IS NULL
CREATE TABLE dbo.Payments
(
    Id            BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Payments PRIMARY KEY,
    OrderId       BIGINT        NOT NULL,
    Amount        DECIMAL(18,2) NOT NULL,
    Method        NVARCHAR(20)  NOT NULL,
    Status        NVARCHAR(20)  NOT NULL CONSTRAINT DF_Payments_Status DEFAULT N'Pending',
    TransactionRef NVARCHAR(64) NULL,
    PaidAt        DATETIME2(3)  NULL,
    CreatedAt     DATETIME2(3)  NOT NULL CONSTRAINT DF_Payments_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_Payments_Orders FOREIGN KEY (OrderId) REFERENCES dbo.Orders (Id),
    CONSTRAINT CK_Payments_Amount CHECK (Amount > 0),
    CONSTRAINT CK_Payments_Method CHECK (Method IN (N'Card', N'Cash', N'BankTransfer')),
    CONSTRAINT CK_Payments_Status CHECK (Status IN (N'Pending', N'Succeeded', N'Failed', N'Refunded'))
);
GO

/* ---------- ProcessedEvents: ідемпотентність споживачів подій (Лаб. 08) ---------- */
IF OBJECT_ID(N'dbo.ProcessedEvents', N'U') IS NULL
CREATE TABLE dbo.ProcessedEvents
(
    EventId     UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_ProcessedEvents PRIMARY KEY,
    EventType   NVARCHAR(200)    NOT NULL,
    ProcessedAt DATETIME2(3)     NOT NULL CONSTRAINT DF_ProcessedEvents_At DEFAULT SYSUTCDATETIME()
);
GO

/* =====================================================================
   Індекси — кожен під конкретний запит
   ===================================================================== */
-- «Замовлення клієнта, новіші першими» (GET /api/orders?customerId=...), фільтр по м'якому видаленню
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Orders_CustomerId_CreatedAt')
    CREATE INDEX IX_Orders_CustomerId_CreatedAt ON dbo.Orders (CustomerId, CreatedAt DESC)
        INCLUDE (Status, TotalAmount) WHERE IsDeleted = 0;
-- «Черга замовлень за статусом» (панель менеджера: всі Confirmed для відвантаження)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Orders_Status')
    CREATE INDEX IX_Orders_Status ON dbo.Orders (Status) INCLUDE (CreatedAt) WHERE IsDeleted = 0;
-- «Позиції замовлення» (завантаження картки замовлення; FK-індекс)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_OrderItems_OrderId')
    CREATE INDEX IX_OrderItems_OrderId ON dbo.OrderItems (OrderId);
-- «У яких замовленнях продавали товар» (аналітика продажів за ProductId)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_OrderItems_ProductId')
    CREATE INDEX IX_OrderItems_ProductId ON dbo.OrderItems (ProductId) INCLUDE (Quantity, UnitPrice);
-- «Історія статусів замовлення» за часом
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_OSH_OrderId_ChangedAt')
    CREATE INDEX IX_OSH_OrderId_ChangedAt ON dbo.OrderStatusHistory (OrderId, ChangedAt);
-- «Платежі замовлення»
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Payments_OrderId')
    CREATE INDEX IX_Payments_OrderId ON dbo.Payments (OrderId) INCLUDE (Status, Amount);
GO

/* ---------- Представлення для читань (денормалізація без дублювання) ---------- */
CREATE OR ALTER VIEW dbo.vw_OrderSummary
AS
SELECT o.Id, o.OrderNumber, o.Status, o.TotalAmount, o.DiscountAmount, o.CreatedAt,
       c.Id AS CustomerId, c.FullName AS CustomerName, c.Email AS CustomerEmail,
       (SELECT COUNT(*) FROM dbo.OrderItems i WHERE i.OrderId = o.Id) AS ItemsCount
FROM dbo.Orders o
JOIN dbo.Customers c ON c.Id = o.CustomerId
WHERE o.IsDeleted = 0;
GO

/* ---------- Найменші привілеї: окремий користувач застосунку ---------- */
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'orders_app')
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'orders_app')
        CREATE LOGIN orders_app WITH PASSWORD = N'Change_me_0rders!', CHECK_POLICY = ON;
    CREATE USER orders_app FOR LOGIN orders_app;
    ALTER ROLE db_datareader ADD MEMBER orders_app;
    ALTER ROLE db_datawriter ADD MEMBER orders_app;
    GRANT EXECUTE ON SCHEMA::dbo TO orders_app;
END
GO
