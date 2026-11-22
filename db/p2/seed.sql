/* Проєкт №2 — ідемпотентні seed-дані CatalogDb */
USE CatalogDb;
GO

MERGE dbo.Categories AS t
USING (VALUES (1, N'Художня література', N'fiction'), (2, N'Українська класика', N'ua-classics'),
              (3, N'Програмування', N'programming'), (4, N'Архітектура ПЗ', N'software-architecture'),
              (5, N'Поезія', N'poetry')) s(Id, Name, Slug)
   ON t.Id = s.Id
WHEN NOT MATCHED THEN INSERT (Id, Name, Slug) VALUES (s.Id, s.Name, s.Slug);
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Publishers WHERE Name = N'Видавництво Старого Лева')
    INSERT INTO dbo.Publishers (Name, Country, Website) VALUES (N'Видавництво Старого Лева', N'Україна', N'https://starylev.com.ua');
IF NOT EXISTS (SELECT 1 FROM dbo.Publishers WHERE Name = N'O''Reilly Media')
    INSERT INTO dbo.Publishers (Name, Country, Website) VALUES (N'O''Reilly Media', N'USA', N'https://www.oreilly.com');
IF NOT EXISTS (SELECT 1 FROM dbo.Publishers WHERE Name = N'Prentice Hall')
    INSERT INTO dbo.Publishers (Name, Country) VALUES (N'Prentice Hall', N'USA');
GO

SET IDENTITY_INSERT dbo.Products ON;
MERGE dbo.Products AS t
USING (VALUES
    (1, N'BK-0001', N'Кобзар', N'Тарас Шевченко', 450.00, 40, N'Видавництво Старого Лева'),
    (2, N'BK-0002', N'Тіні забутих предків', N'Михайло Коцюбинський', 320.00, 25, N'Видавництво Старого Лева'),
    (3, N'BK-0003', N'Clean Code', N'Robert C. Martin', 1250.00, 12, N'Prentice Hall'),
    (4, N'BK-0004', N'Designing Data-Intensive Applications', N'Martin Kleppmann', 1890.00, 8, N'O''Reilly Media'),
    (5, N'BK-0005', N'Лісова пісня', N'Леся Українка', 280.00, 30, N'Видавництво Старого Лева')
) s(Id, Sku, Name, Author, Price, Stock, Publisher)
   ON t.Id = s.Id
WHEN NOT MATCHED THEN
    INSERT (Id, Sku, Name, Author, Price, StockQuantity, PublisherId)
    VALUES (s.Id, s.Sku, s.Name, s.Author, s.Price, s.Stock, (SELECT Id FROM dbo.Publishers WHERE Name = s.Publisher));
SET IDENTITY_INSERT dbo.Products OFF;
GO

MERGE dbo.ProductDetails AS t
USING (VALUES (1, N'978-966-448-001-1', 704, N'uk', 2019, N'Повне видання поезій'),
              (2, N'978-966-448-002-8', 192, N'uk', 2020, NULL),
              (3, N'978-0-13-235088-4', 464, N'en', 2008, N'A Handbook of Agile Software Craftsmanship'),
              (4, N'978-1-4493-7332-0', 616, N'en', 2017, N'The big ideas behind reliable, scalable systems'),
              (5, N'978-966-448-005-9', 160, N'uk', 2021, NULL)) s(ProductId, Isbn, Pages, Lang, Yr, Descr)
   ON t.ProductId = s.ProductId
WHEN NOT MATCHED THEN INSERT (ProductId, Isbn, Pages, Language, PublishedYear, Description)
     VALUES (s.ProductId, s.Isbn, s.Pages, s.Lang, s.Yr, s.Descr);
GO

MERGE dbo.ProductCategories AS t
USING (VALUES (1, 2, 1), (1, 5, 0), (2, 1, 0), (2, 2, 1), (3, 3, 1), (4, 3, 0), (4, 4, 1), (5, 2, 1), (5, 5, 0)) s(ProductId, CategoryId, IsPrimary)
   ON t.ProductId = s.ProductId AND t.CategoryId = s.CategoryId
WHEN NOT MATCHED THEN INSERT (ProductId, CategoryId, IsPrimary) VALUES (s.ProductId, s.CategoryId, s.IsPrimary);
GO
