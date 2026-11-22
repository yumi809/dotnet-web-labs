using Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Catalog.Dal.Repositories;

public interface IProductRepository : IGenericRepository<Product>
{
    /// <summary>Eager loading: Include/ThenInclude — картка книги з видавництвом, деталями та категоріями.</summary>
    Task<Product?> GetWithDetailsAsync(long id, CancellationToken ct = default);
    Task<bool> SkuExistsAsync(string sku, long? exceptId = null, CancellationToken ct = default);
}

public interface IPublisherRepository : IGenericRepository<Publisher>
{
    /// <summary>Explicit loading: Entry(...).Collection(...).LoadAsync окремим запитом.</summary>
    Task<Publisher?> GetWithProductsAsync(int id, CancellationToken ct = default);
    Task<bool> NameExistsAsync(string name, CancellationToken ct = default);
}

public sealed record CategoryProductRow(long ProductId, string ProductName, decimal Price, DateTime AddedAt, bool IsPrimary);

public interface ICategoryRepository : IGenericRepository<Category>
{
    /// <summary>LINQ-запит M:N через проміжну сутність з використанням її власного поля AddedAt.</summary>
    Task<IReadOnlyList<CategoryProductRow>> GetProductsAddedSinceAsync(int categoryId, DateTime since, CancellationToken ct = default);
}

public sealed class ProductRepository(CatalogDbContext context) : GenericRepository<Product>(context), IProductRepository
{
    public Task<Product?> GetWithDetailsAsync(long id, CancellationToken ct = default) =>
        Set.Include(p => p.Publisher)
           .Include(p => p.Detail)
           .Include(p => p.Categories).ThenInclude(pc => pc.Category)
           .AsSplitQuery()
           .FirstOrDefaultAsync(p => p.Id == id, ct);

    public Task<bool> SkuExistsAsync(string sku, long? exceptId = null, CancellationToken ct = default) =>
        Set.AnyAsync(p => p.Sku == sku && (exceptId == null || p.Id != exceptId), ct);
}

public sealed class PublisherRepository(CatalogDbContext context) : GenericRepository<Publisher>(context), IPublisherRepository
{
    public async Task<Publisher?> GetWithProductsAsync(int id, CancellationToken ct = default)
    {
        var publisher = await Set.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (publisher is null) return null;

        // Явне завантаження колекції — окремий SQL-запит лише коли він потрібен
        await Context.Entry(publisher)
            .Collection(p => p.Products)
            .Query()
            .Where(p => p.IsActive)
            .OrderBy(p => p.Name)
            .LoadAsync(ct);
        return publisher;
    }

    public Task<bool> NameExistsAsync(string name, CancellationToken ct = default) =>
        Set.AnyAsync(p => p.Name == name, ct);
}

public sealed class CategoryRepository(CatalogDbContext context) : GenericRepository<Category>(context), ICategoryRepository
{
    public async Task<IReadOnlyList<CategoryProductRow>> GetProductsAddedSinceAsync(int categoryId, DateTime since, CancellationToken ct = default) =>
        await Context.ProductCategories
            .AsNoTracking()
            .Where(pc => pc.CategoryId == categoryId && pc.AddedAt >= since && pc.Product.IsActive)
            .OrderByDescending(pc => pc.AddedAt)
            .Select(pc => new CategoryProductRow(pc.ProductId, pc.Product.Name, pc.Product.Price, pc.AddedAt, pc.IsPrimary))
            .ToListAsync(ct);
}
