using Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Catalog.Dal.Seeding;

/// <summary>
/// Програмний ідемпотентний сідінг після MigrateAsync: довідники з фіксованими Id вже додані HasData,
/// тут — видавництва й книги з перевіркою наявності (повторний запуск нічого не дублює).
/// Вимикається налаштуванням Catalog:SeedData=false (у production).
/// </summary>
public sealed class DbInitializer(CatalogDbContext context, ILogger<DbInitializer> logger)
{
    public async Task InitializeAsync(bool seedData, CancellationToken ct = default)
    {
        await context.Database.MigrateAsync(ct);
        logger.LogInformation("CatalogDb migrations applied");
        if (!seedData) return;

        var publishers = new[]
        {
            new Publisher("Видавництво Старого Лева", "Україна", "https://starylev.com.ua"),
            new Publisher("O'Reilly Media", "USA", "https://www.oreilly.com"),
            new Publisher("Prentice Hall", "USA")
        };
        foreach (var publisher in publishers)
        {
            if (!await context.Publishers.AnyAsync(p => p.Name == publisher.Name, ct))
                context.Publishers.Add(publisher);
        }
        await context.SaveChangesAsync(ct);

        var pubIds = await context.Publishers.ToDictionaryAsync(p => p.Name, p => p.Id, ct);
        var books = new (string Sku, string Name, string Author, decimal Price, int Stock, string Publisher, string Isbn, int Pages, string Lang, int Year, int[] Cats)[]
        {
            ("BK-0001", "Кобзар", "Тарас Шевченко", 450m, 40, "Видавництво Старого Лева", "978-966-448-001-1", 704, "uk", 2019, [2, 5]),
            ("BK-0002", "Тіні забутих предків", "Михайло Коцюбинський", 320m, 25, "Видавництво Старого Лева", "978-966-448-002-8", 192, "uk", 2020, [2, 1]),
            ("BK-0003", "Clean Code", "Robert C. Martin", 1250m, 12, "Prentice Hall", "978-0-13-235088-4", 464, "en", 2008, [3]),
            ("BK-0004", "Designing Data-Intensive Applications", "Martin Kleppmann", 1890m, 8, "O'Reilly Media", "978-1-4493-7332-0", 616, "en", 2017, [4, 3]),
            ("BK-0005", "Лісова пісня", "Леся Українка", 280m, 30, "Видавництво Старого Лева", "978-966-448-005-9", 160, "uk", 2021, [2, 5])
        };

        var added = 0;
        foreach (var b in books)
        {
            if (await context.Products.AnyAsync(p => p.Sku == b.Sku, ct)) continue;
            var product = new Product(b.Sku, b.Name, b.Author, b.Price, b.Stock, pubIds[b.Publisher]);
            product.SetDetail(b.Isbn, b.Pages, b.Lang, b.Year, null);
            for (var i = 0; i < b.Cats.Length; i++) product.AssignCategory(b.Cats[i], isPrimary: i == 0);
            context.Products.Add(product);
            added++;
        }
        await context.SaveChangesAsync(ct);
        logger.LogInformation("Catalog seeding finished, {Added} products added", added);
    }
}
