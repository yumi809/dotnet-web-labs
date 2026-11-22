IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122231633_InitialCreate'
)
BEGIN
    CREATE TABLE [Categories] (
        [Id] int NOT NULL,
        [Name] nvarchar(100) NOT NULL,
        [Slug] nvarchar(100) NOT NULL,
        CONSTRAINT [PK_Categories] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122231633_InitialCreate'
)
BEGIN
    CREATE TABLE [Publishers] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(150) NOT NULL,
        [Country] nvarchar(80) NOT NULL,
        [Website] nvarchar(200) NULL,
        CONSTRAINT [PK_Publishers] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122231633_InitialCreate'
)
BEGIN
    CREATE TABLE [Products] (
        [Id] bigint NOT NULL IDENTITY,
        [Sku] nvarchar(32) NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        [Author] nvarchar(200) NOT NULL,
        [Price] decimal(18,2) NOT NULL,
        [StockQuantity] int NOT NULL DEFAULT 0,
        [PublisherId] int NOT NULL,
        [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit),
        [CreatedAt] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
        [UpdatedAt] datetime2 NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_Products] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Products_Price] CHECK ([Price] > 0),
        CONSTRAINT [CK_Products_Stock] CHECK ([StockQuantity] >= 0),
        CONSTRAINT [FK_Products_Publishers_PublisherId] FOREIGN KEY ([PublisherId]) REFERENCES [Publishers] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122231633_InitialCreate'
)
BEGIN
    CREATE TABLE [ProductCategories] (
        [ProductId] bigint NOT NULL,
        [CategoryId] int NOT NULL,
        [AddedAt] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
        [IsPrimary] bit NOT NULL DEFAULT CAST(0 AS bit),
        CONSTRAINT [PK_ProductCategories] PRIMARY KEY ([ProductId], [CategoryId]),
        CONSTRAINT [FK_ProductCategories_Categories_CategoryId] FOREIGN KEY ([CategoryId]) REFERENCES [Categories] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_ProductCategories_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Products] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122231633_InitialCreate'
)
BEGIN
    CREATE TABLE [ProductDetails] (
        [ProductId] bigint NOT NULL,
        [Isbn] nvarchar(17) NOT NULL,
        [Pages] int NOT NULL,
        [Language] nvarchar(30) NOT NULL,
        [PublishedYear] int NOT NULL,
        [Description] nvarchar(2000) NULL,
        CONSTRAINT [PK_ProductDetails] PRIMARY KEY ([ProductId]),
        CONSTRAINT [CK_ProductDetails_Pages] CHECK ([Pages] > 0),
        CONSTRAINT [CK_ProductDetails_Year] CHECK ([PublishedYear] BETWEEN 1450 AND 2100),
        CONSTRAINT [FK_ProductDetails_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Products] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122231633_InitialCreate'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Name', N'Slug') AND [object_id] = OBJECT_ID(N'[Categories]'))
        SET IDENTITY_INSERT [Categories] ON;
    EXEC(N'INSERT INTO [Categories] ([Id], [Name], [Slug])
    VALUES (1, N''Художня література'', N''fiction''),
    (2, N''Українська класика'', N''ua-classics''),
    (3, N''Програмування'', N''programming''),
    (4, N''Архітектура ПЗ'', N''software-architecture''),
    (5, N''Поезія'', N''poetry'')');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Name', N'Slug') AND [object_id] = OBJECT_ID(N'[Categories]'))
        SET IDENTITY_INSERT [Categories] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122231633_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [UQ_Categories_Slug] ON [Categories] ([Slug]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122231633_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_ProductCategories_CategoryId_AddedAt] ON [ProductCategories] ([CategoryId], [AddedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122231633_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [UQ_ProductDetails_Isbn] ON [ProductDetails] ([Isbn]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122231633_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Products_IsActive_Price] ON [Products] ([IsActive], [Price]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122231633_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Products_Name] ON [Products] ([Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122231633_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Products_PublisherId] ON [Products] ([PublisherId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122231633_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [UQ_Products_Sku] ON [Products] ([Sku]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122231633_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [UQ_Publishers_Name] ON [Publishers] ([Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122231633_InitialCreate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261122231633_InitialCreate', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122234033_AddProcessedEvents'
)
BEGIN
    CREATE TABLE [ProcessedEvents] (
        [EventId] uniqueidentifier NOT NULL,
        [EventType] nvarchar(200) NOT NULL,
        [ProcessedAt] datetime2 NOT NULL DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [PK_ProcessedEvents] PRIMARY KEY ([EventId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261122234033_AddProcessedEvents'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261122234033_AddProcessedEvents', N'10.0.11');
END;

COMMIT;
GO

