using Catalog.Bll.Dto;
using Catalog.Dal.Specifications;
using Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Catalog.Bll.Specifications;

/// <summary>
/// Фільтр + сортування + пагінація каталогу. Сортування — лише за білим списком полів,
/// тому довільний рядок від клієнта не потрапляє в запит.
/// </summary>
public sealed class ProductFilterSpecification : Specification<Product>
{
    public ProductFilterSpecification(ProductQueryDto q, bool paged = true)
    {
        Where(p => p.IsActive);
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var term = q.Search.Trim();
            Where(p => p.Name.Contains(term) || p.Author.Contains(term));
        }
        if (q.CategoryId is { } categoryId) Where(p => p.Categories.Any(c => c.CategoryId == categoryId));
        if (q.PublisherId is { } publisherId) Where(p => p.PublisherId == publisherId);
        if (q.MinPrice is { } min) Where(p => p.Price >= min);
        if (q.MaxPrice is { } max) Where(p => p.Price <= max);
        if (q.InStock == true) Where(p => p.StockQuantity > 0);

        Include(query => query.Include(p => p.Publisher));

        var desc = q.SortDir == "desc";
        ApplyOrderBy(q.SortBy.ToLowerInvariant() switch
        {
            "price" => desc ? s => s.OrderByDescending(p => p.Price).ThenBy(p => p.Id) : s => s.OrderBy(p => p.Price).ThenBy(p => p.Id),
            "createdat" => desc ? s => s.OrderByDescending(p => p.CreatedAt).ThenBy(p => p.Id) : s => s.OrderBy(p => p.CreatedAt).ThenBy(p => p.Id),
            "stock" => desc ? s => s.OrderByDescending(p => p.StockQuantity).ThenBy(p => p.Id) : s => s.OrderBy(p => p.StockQuantity).ThenBy(p => p.Id),
            _ => desc ? s => s.OrderByDescending(p => p.Name).ThenBy(p => p.Id) : s => s.OrderBy(p => p.Name).ThenBy(p => p.Id)
        });

        if (paged) ApplyPaging((q.Page - 1) * q.PageSize, q.PageSize);
    }
}

/// <summary>Книги з низьким залишком для менеджера складу.</summary>
public sealed class LowStockSpecification : Specification<Product>
{
    public LowStockSpecification(int threshold)
    {
        Where(p => p.IsActive && p.StockQuantity <= threshold);
        Include(query => query.Include(p => p.Publisher));
        ApplyOrderBy(s => s.OrderBy(p => p.StockQuantity));
    }
}
