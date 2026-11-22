/* =====================================================================
   Проєкт №1 — збережувані процедури OrdersDb
   ===================================================================== */
USE OrdersDb;
GO

/* 1. Отримати замовлення з позиціями (два набори результатів) */
CREATE OR ALTER PROCEDURE dbo.usp_Order_GetWithItems
    @OrderId BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT Id, OrderNumber, CustomerId, Status, TotalAmount, DiscountAmount, Comment,
           CreatedAt, CreatedBy, UpdatedAt, UpdatedBy, RowVersion
    FROM dbo.Orders WHERE Id = @OrderId AND IsDeleted = 0;

    SELECT Id, OrderId, ProductId, ProductName, UnitPrice, Quantity, LineTotal
    FROM dbo.OrderItems WHERE OrderId = @OrderId;
END
GO

/* 2. Створити замовлення (CRUD: Create) — повертає Id */
CREATE OR ALTER PROCEDURE dbo.usp_Order_Create
    @CustomerId BIGINT,
    @Comment    NVARCHAR(500) = NULL,
    @CreatedBy  NVARCHAR(100) = N'system',
    @OrderId    BIGINT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM dbo.Customers WHERE Id = @CustomerId AND IsDeleted = 0)
        THROW 50404, N'Customer not found', 1;

    DECLARE @Number NVARCHAR(20) = CONCAT(N'ORD-', FORMAT(SYSUTCDATETIME(), 'yyMMdd'), N'-',
                                          RIGHT(CONCAT(N'0000', ABS(CHECKSUM(NEWID())) % 10000), 4));
    INSERT INTO dbo.Orders (OrderNumber, CustomerId, Comment, CreatedBy)
    VALUES (@Number, @CustomerId, @Comment, @CreatedBy);
    SET @OrderId = SCOPE_IDENTITY();

    INSERT INTO dbo.OrderStatusHistory (OrderId, FromStatus, ToStatus, ChangedBy, Reason)
    VALUES (@OrderId, NULL, N'New', @CreatedBy, N'Order created');
END
GO

/* 3. Додати позицію: ціна й назва — знімок з локального довідника Products */
CREATE OR ALTER PROCEDURE dbo.usp_Order_AddItem
    @OrderId   BIGINT,
    @ProductId BIGINT,
    @Quantity  INT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;
        DECLARE @Status NVARCHAR(20) = (SELECT Status FROM dbo.Orders WITH (UPDLOCK) WHERE Id = @OrderId AND IsDeleted = 0);
        IF @Status IS NULL THROW 50404, N'Order not found', 1;
        IF @Status <> N'New' THROW 50409, N'Items can be added only to orders in status New', 1;

        DECLARE @Name NVARCHAR(200), @Price DECIMAL(18,2);
        SELECT @Name = Name, @Price = Price FROM dbo.Products WHERE ProductId = @ProductId AND IsAvailable = 1;
        IF @Name IS NULL THROW 50404, N'Product not found or unavailable', 1;

        MERGE dbo.OrderItems AS t
        USING (SELECT @OrderId AS OrderId, @ProductId AS ProductId) AS s
           ON t.OrderId = s.OrderId AND t.ProductId = s.ProductId
        WHEN MATCHED THEN UPDATE SET Quantity = t.Quantity + @Quantity
        WHEN NOT MATCHED THEN INSERT (OrderId, ProductId, ProductName, UnitPrice, Quantity)
             VALUES (@OrderId, @ProductId, @Name, @Price, @Quantity);

        UPDATE dbo.Orders
           SET TotalAmount = (SELECT SUM(LineTotal) FROM dbo.OrderItems WHERE OrderId = @OrderId),
               UpdatedAt = SYSUTCDATETIME()
         WHERE Id = @OrderId;
    COMMIT TRANSACTION;
END
GO

/* 4. Транзакційна зміна статусу з перевіркою допустимого переходу та rowversion */
CREATE OR ALTER PROCEDURE dbo.usp_Order_ChangeStatus
    @OrderId    BIGINT,
    @NewStatus  NVARCHAR(20),
    @ChangedBy  NVARCHAR(100) = N'system',
    @Reason     NVARCHAR(300) = NULL,
    @RowVersion BINARY(8) = NULL          -- якщо передано — перевірка оптимістичної конкурентності
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRY
        BEGIN TRANSACTION;

        DECLARE @Current NVARCHAR(20), @CurrentRv BINARY(8);
        SELECT @Current = Status, @CurrentRv = RowVersion
        FROM dbo.Orders WITH (UPDLOCK, ROWLOCK)
        WHERE Id = @OrderId AND IsDeleted = 0;

        IF @Current IS NULL
            THROW 50404, N'Order not found', 1;
        IF @RowVersion IS NOT NULL AND @RowVersion <> @CurrentRv
            THROW 50412, N'Order was modified by another user (concurrency conflict)', 1;

        -- матриця допустимих переходів
        IF NOT (
               (@Current = N'New'       AND @NewStatus IN (N'Confirmed', N'Cancelled'))
            OR (@Current = N'Confirmed' AND @NewStatus IN (N'Paid', N'Cancelled'))
            OR (@Current = N'Paid'      AND @NewStatus IN (N'Shipped', N'Cancelled'))
            OR (@Current = N'Shipped'   AND @NewStatus = N'Completed'))
        BEGIN
            DECLARE @Msg NVARCHAR(200) = CONCAT(N'Invalid status transition ', @Current, N' -> ', @NewStatus);
            THROW 50409, @Msg, 1;
        END

        IF @NewStatus = N'Confirmed' AND NOT EXISTS (SELECT 1 FROM dbo.OrderItems WHERE OrderId = @OrderId)
            THROW 50409, N'Cannot confirm an empty order', 1;

        UPDATE dbo.Orders
           SET Status = @NewStatus, UpdatedAt = SYSUTCDATETIME(), UpdatedBy = @ChangedBy
         WHERE Id = @OrderId;

        INSERT INTO dbo.OrderStatusHistory (OrderId, FromStatus, ToStatus, ChangedBy, Reason)
        VALUES (@OrderId, @Current, @NewStatus, @ChangedBy, @Reason);

        COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
        THROW;   -- керована помилка 504xx передається клієнту
    END CATCH
END
GO

/* 5. Застосувати промокод (M:N Orders–Promotions) */
CREATE OR ALTER PROCEDURE dbo.usp_Order_ApplyPromotion
    @OrderId BIGINT,
    @Code    NVARCHAR(30)
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;
    BEGIN TRANSACTION;
        DECLARE @PromoId INT, @Percent DECIMAL(5,2);
        SELECT @PromoId = Id, @Percent = DiscountPercent
        FROM dbo.Promotions
        WHERE Code = @Code AND IsActive = 1 AND CAST(SYSUTCDATETIME() AS DATE) BETWEEN ValidFrom AND ValidTo;
        IF @PromoId IS NULL THROW 50404, N'Promotion not found or expired', 1;
        IF EXISTS (SELECT 1 FROM dbo.OrderPromotions WHERE OrderId = @OrderId AND PromotionId = @PromoId)
            THROW 50409, N'Promotion already applied', 1;

        DECLARE @Total DECIMAL(18,2) = (SELECT TotalAmount FROM dbo.Orders WITH (UPDLOCK) WHERE Id = @OrderId AND Status = N'New');
        IF @Total IS NULL THROW 50409, N'Promotion can be applied only to New orders', 1;

        DECLARE @Discount DECIMAL(18,2) = ROUND(@Total * @Percent / 100, 2);
        INSERT INTO dbo.OrderPromotions (OrderId, PromotionId, DiscountAmount) VALUES (@OrderId, @PromoId, @Discount);
        UPDATE dbo.Orders SET DiscountAmount = DiscountAmount + @Discount, UpdatedAt = SYSUTCDATETIME() WHERE Id = @OrderId;
    COMMIT TRANSACTION;
END
GO

/* 6. Звіт: замовлення клієнта з агрегатами (м'яке видалення враховано) */
CREATE OR ALTER PROCEDURE dbo.usp_Customer_GetOrderStats
    @CustomerId BIGINT
AS
BEGIN
    SET NOCOUNT ON;
    SELECT c.Id AS CustomerId, c.FullName,
           COUNT(o.Id) AS OrdersCount,
           ISNULL(SUM(o.TotalAmount - o.DiscountAmount), 0) AS TotalSpent,
           MAX(o.CreatedAt) AS LastOrderAt
    FROM dbo.Customers c
    LEFT JOIN dbo.Orders o ON o.CustomerId = c.Id AND o.IsDeleted = 0 AND o.Status <> N'Cancelled'
    WHERE c.Id = @CustomerId AND c.IsDeleted = 0
    GROUP BY c.Id, c.FullName;
END
GO

/* М'яке видалення замовлення (лише New/Cancelled) */
CREATE OR ALTER PROCEDURE dbo.usp_Order_SoftDelete
    @OrderId BIGINT,
    @DeletedBy NVARCHAR(100) = N'system'
AS
BEGIN
    SET NOCOUNT ON;
    UPDATE dbo.Orders SET IsDeleted = 1, UpdatedAt = SYSUTCDATETIME(), UpdatedBy = @DeletedBy
    WHERE Id = @OrderId AND IsDeleted = 0 AND Status IN (N'New', N'Cancelled');
    IF @@ROWCOUNT = 0 THROW 50409, N'Only New or Cancelled orders can be deleted', 1;
END
GO
