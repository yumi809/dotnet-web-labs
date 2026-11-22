/* Проєкт №1 — ідемпотентні seed-дані OrdersDb: кожна вставка перевіряє наявність запису */
USE OrdersDb;
GO

-- Клієнти
IF NOT EXISTS (SELECT 1 FROM dbo.Customers WHERE Email = N'olena.koval@example.com')
    INSERT INTO dbo.Customers (Email, FullName, Phone) VALUES (N'olena.koval@example.com', N'Олена Коваль', N'+380501112233');
IF NOT EXISTS (SELECT 1 FROM dbo.Customers WHERE Email = N'taras.bondar@example.com')
    INSERT INTO dbo.Customers (Email, FullName, Phone) VALUES (N'taras.bondar@example.com', N'Тарас Бондар', N'+380672223344');
IF NOT EXISTS (SELECT 1 FROM dbo.Customers WHERE Email = N'iryna.melnyk@example.com')
    INSERT INTO dbo.Customers (Email, FullName) VALUES (N'iryna.melnyk@example.com', N'Ірина Мельник');
GO

-- Профілі (1:1)
INSERT INTO dbo.CustomerProfiles (CustomerId, ShippingCity, ShippingAddress, PostalCode, LoyaltyLevel)
SELECT c.Id, v.City, v.Addr, v.Zip, v.Lvl
FROM (VALUES (N'olena.koval@example.com', N'Чернівці', N'вул. Головна, 10', N'58000', N'Gold'),
             (N'taras.bondar@example.com', N'Київ', N'просп. Перемоги, 25', N'03057', N'Silver')) v(Email, City, Addr, Zip, Lvl)
JOIN dbo.Customers c ON c.Email = v.Email
WHERE NOT EXISTS (SELECT 1 FROM dbo.CustomerProfiles p WHERE p.CustomerId = c.Id);
GO

-- Локальний довідник товарів (копія з CatalogDb; Id збігаються з db/p2/seed.sql)
MERGE dbo.Products AS t
USING (VALUES (1, N'Кобзар', 450.00), (2, N'Тіні забутих предків', 320.00),
              (3, N'Clean Code', 1250.00), (4, N'Designing Data-Intensive Applications', 1890.00),
              (5, N'Лісова пісня', 280.00)) s(ProductId, Name, Price)
   ON t.ProductId = s.ProductId
WHEN NOT MATCHED THEN INSERT (ProductId, Name, Price) VALUES (s.ProductId, s.Name, s.Price);
GO

-- Промокоди
IF NOT EXISTS (SELECT 1 FROM dbo.Promotions WHERE Code = N'WELCOME10')
    INSERT INTO dbo.Promotions (Code, Description, DiscountPercent, ValidFrom, ValidTo)
    VALUES (N'WELCOME10', N'Знижка 10% на перше замовлення', 10, '2026-01-01', '2027-12-31');
IF NOT EXISTS (SELECT 1 FROM dbo.Promotions WHERE Code = N'BOOKWEEK')
    INSERT INTO dbo.Promotions (Code, Description, DiscountPercent, ValidFrom, ValidTo)
    VALUES (N'BOOKWEEK', N'Тиждень книги', 15, '2026-04-20', '2026-04-27');
GO

-- Замовлення із позиціями (через фіксований OrderNumber для ідемпотентності)
DECLARE @Olena BIGINT = (SELECT Id FROM dbo.Customers WHERE Email = N'olena.koval@example.com');
DECLARE @Taras BIGINT = (SELECT Id FROM dbo.Customers WHERE Email = N'taras.bondar@example.com');

IF NOT EXISTS (SELECT 1 FROM dbo.Orders WHERE OrderNumber = N'ORD-SEED-0001')
BEGIN
    INSERT INTO dbo.Orders (OrderNumber, CustomerId, Status, TotalAmount) VALUES (N'ORD-SEED-0001', @Olena, N'Paid', 1220.00);
    DECLARE @O1 BIGINT = SCOPE_IDENTITY();
    INSERT INTO dbo.OrderItems (OrderId, ProductId, ProductName, UnitPrice, Quantity)
    VALUES (@O1, 1, N'Кобзар', 450.00, 2), (@O1, 2, N'Тіні забутих предків', 320.00, 1);
    INSERT INTO dbo.OrderStatusHistory (OrderId, FromStatus, ToStatus, Reason)
    VALUES (@O1, NULL, N'New', N'seed'), (@O1, N'New', N'Confirmed', N'seed'), (@O1, N'Confirmed', N'Paid', N'seed');
    INSERT INTO dbo.Payments (OrderId, Amount, Method, Status, TransactionRef, PaidAt)
    VALUES (@O1, 1220.00, N'Card', N'Succeeded', N'TX-SEED-1', SYSUTCDATETIME());
END

IF NOT EXISTS (SELECT 1 FROM dbo.Orders WHERE OrderNumber = N'ORD-SEED-0002')
BEGIN
    INSERT INTO dbo.Orders (OrderNumber, CustomerId, Status, TotalAmount) VALUES (N'ORD-SEED-0002', @Taras, N'New', 1250.00);
    DECLARE @O2 BIGINT = SCOPE_IDENTITY();
    INSERT INTO dbo.OrderItems (OrderId, ProductId, ProductName, UnitPrice, Quantity)
    VALUES (@O2, 3, N'Clean Code', 1250.00, 1);
    INSERT INTO dbo.OrderStatusHistory (OrderId, FromStatus, ToStatus, Reason) VALUES (@O2, NULL, N'New', N'seed');
    INSERT INTO dbo.OrderPromotions (OrderId, PromotionId, DiscountAmount)
    SELECT @O2, Id, 125.00 FROM dbo.Promotions WHERE Code = N'WELCOME10';
    UPDATE dbo.Orders SET DiscountAmount = 125.00 WHERE Id = @O2;
END
GO
