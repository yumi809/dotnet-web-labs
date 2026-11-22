/* =====================================================================
   Проєкт №2 — CatalogDb (контекст «Каталог книг»)
   Технологія: EF Core Code First (Лаб. 03). Цей скрипт відповідає міграції InitialCreate
   і дозволяє розгорнути схему без dotnet ef.
   ===================================================================== */
IF DB_ID(N'CatalogDb') IS NULL
    CREATE DATABASE CatalogDb;
GO
USE CatalogDb;
GO

/* Publishers — 1:N до Products */
IF OBJECT_ID(N'dbo.Publishers', N'U') IS NULL
CREATE TABLE dbo.Publishers
(
    Id       INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Publishers PRIMARY KEY,
    Name     NVARCHAR(150) NOT NULL,
    Country  NVARCHAR(80)  NOT NULL,
    Website  NVARCHAR(200) NULL,
    CONSTRAINT UQ_Publishers_Name UNIQUE (Name)
);
GO

/* Categories — довідник (фіксовані Id, HasData) */
IF OBJECT_ID(N'dbo.Categories', N'U') IS NULL
CREATE TABLE dbo.Categories
(
    Id    INT           NOT NULL CONSTRAINT PK_Categories PRIMARY KEY,
    Name  NVARCHAR(100) NOT NULL,
    Slug  NVARCHAR(100) NOT NULL,
    CONSTRAINT UQ_Categories_Slug UNIQUE (Slug)
);
GO

/* Products — книги */
IF OBJECT_ID(N'dbo.Products', N'U') IS NULL
CREATE TABLE dbo.Products
(
    Id            BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Products PRIMARY KEY,
    Sku           NVARCHAR(32)  NOT NULL,
    Name          NVARCHAR(200) NOT NULL,
    Author        NVARCHAR(200) NOT NULL,
    Price         DECIMAL(18,2) NOT NULL,
    StockQuantity INT           NOT NULL CONSTRAINT DF_Products_Stock DEFAULT 0,
    PublisherId   INT           NOT NULL,
    IsActive      BIT           NOT NULL CONSTRAINT DF_Products_IsActive DEFAULT 1,
    CreatedAt     DATETIME2(3)  NOT NULL CONSTRAINT DF_Products_CreatedAt DEFAULT SYSUTCDATETIME(),
    UpdatedAt     DATETIME2(3)  NULL,
    RowVersion    ROWVERSION    NOT NULL,
    CONSTRAINT UQ_Products_Sku UNIQUE (Sku),
    CONSTRAINT FK_Products_Publishers FOREIGN KEY (PublisherId) REFERENCES dbo.Publishers (Id),
    CONSTRAINT CK_Products_Price CHECK (Price > 0),
    CONSTRAINT CK_Products_Stock CHECK (StockQuantity >= 0)
);
GO

/* ProductDetails — 1:1 (FK = PK) */
IF OBJECT_ID(N'dbo.ProductDetails', N'U') IS NULL
CREATE TABLE dbo.ProductDetails
(
    ProductId    BIGINT         NOT NULL CONSTRAINT PK_ProductDetails PRIMARY KEY,
    Isbn         NVARCHAR(17)   NOT NULL,
    Pages        INT            NOT NULL,
    Language     NVARCHAR(30)   NOT NULL,
    PublishedYear INT           NOT NULL,
    Description  NVARCHAR(2000) NULL,
    CONSTRAINT FK_ProductDetails_Products FOREIGN KEY (ProductId) REFERENCES dbo.Products (Id) ON DELETE CASCADE,
    CONSTRAINT UQ_ProductDetails_Isbn UNIQUE (Isbn),
    CONSTRAINT CK_ProductDetails_Pages CHECK (Pages > 0),
    CONSTRAINT CK_ProductDetails_Year CHECK (PublishedYear BETWEEN 1450 AND 2100)
);
GO

/* ProductCategories — M:N з власним полем AddedAt */
IF OBJECT_ID(N'dbo.ProductCategories', N'U') IS NULL
CREATE TABLE dbo.ProductCategories
(
    ProductId  BIGINT       NOT NULL,
    CategoryId INT          NOT NULL,
    AddedAt    DATETIME2(3) NOT NULL CONSTRAINT DF_ProductCategories_AddedAt DEFAULT SYSUTCDATETIME(),
    IsPrimary  BIT          NOT NULL CONSTRAINT DF_ProductCategories_IsPrimary DEFAULT 0,
    CONSTRAINT PK_ProductCategories PRIMARY KEY (ProductId, CategoryId),
    CONSTRAINT FK_ProductCategories_Products FOREIGN KEY (ProductId) REFERENCES dbo.Products (Id) ON DELETE CASCADE,
    CONSTRAINT FK_ProductCategories_Categories FOREIGN KEY (CategoryId) REFERENCES dbo.Categories (Id)
);
GO

/* Індекси під запити каталогу */
-- пошук/сортування за назвою у списку товарів
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Products_Name')
    CREATE INDEX IX_Products_Name ON dbo.Products (Name);
-- фільтр за ціновим діапазоном серед активних
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Products_IsActive_Price')
    CREATE INDEX IX_Products_IsActive_Price ON dbo.Products (IsActive, Price);
-- «Книги видавництва»
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Products_PublisherId')
    CREATE INDEX IX_Products_PublisherId ON dbo.Products (PublisherId);
-- «Книги категорії, нещодавно додані»
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_ProductCategories_CategoryId_AddedAt')
    CREATE INDEX IX_ProductCategories_CategoryId_AddedAt ON dbo.ProductCategories (CategoryId, AddedAt DESC);
GO
