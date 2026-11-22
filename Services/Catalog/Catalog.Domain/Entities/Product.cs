using Catalog.Domain.Exceptions;

namespace Catalog.Domain.Entities;

/// <summary>
/// Книга в каталозі. Інкапсуляція: приватні setter-и, валідація в конструкторі,
/// зміна стану лише методами (AddStock / RemoveStock / ChangePrice ...).
/// </summary>
public class Product
{
    private readonly List<ProductCategory> _categories = [];

    public long Id { get; private set; }
    public string Sku { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string Author { get; private set; } = null!;
    public decimal Price { get; private set; }
    public int StockQuantity { get; private set; }
    public int PublisherId { get; private set; }
    public Publisher? Publisher { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    /// <summary>1:1 — деталі книги.</summary>
    public ProductDetail? Detail { get; private set; }

    /// <summary>M:N з категоріями через явну проміжну сутність.</summary>
    public IReadOnlyCollection<ProductCategory> Categories => _categories.AsReadOnly();

    private Product() { } // для EF Core

    public Product(string sku, string name, string author, decimal price, int stockQuantity, int publisherId)
    {
        if (string.IsNullOrWhiteSpace(sku)) throw new DomainException("SKU is required");
        if (stockQuantity < 0) throw new DomainException("Stock quantity cannot be negative");
        if (publisherId <= 0) throw new DomainException("Publisher is required");

        Sku = sku.Trim().ToUpperInvariant();
        Rename(name, author);
        ChangePrice(price);
        StockQuantity = stockQuantity;
        PublisherId = publisherId;
        CreatedAt = DateTime.UtcNow;
    }

    public void Rename(string name, string author)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200) throw new DomainException("Name must be 1..200 characters");
        if (string.IsNullOrWhiteSpace(author) || author.Length > 200) throw new DomainException("Author must be 1..200 characters");
        Name = name.Trim();
        Author = author.Trim();
        Touch();
    }

    public void ChangePrice(decimal newPrice)
    {
        if (newPrice <= 0) throw new DomainException("Price must be greater than zero");
        if (decimal.Round(newPrice, 2) != newPrice) throw new DomainException("Price must have at most 2 decimal places");
        Price = newPrice;
        Touch();
    }

    public void ChangePublisher(int publisherId)
    {
        if (publisherId <= 0) throw new DomainException("Publisher is required");
        PublisherId = publisherId;
        Touch();
    }

    public void AddStock(int quantity)
    {
        if (quantity <= 0) throw new DomainException("Quantity to add must be positive");
        StockQuantity += quantity;
        Touch();
    }

    public void RemoveStock(int quantity)
    {
        if (quantity <= 0) throw new DomainException("Quantity to remove must be positive");
        if (quantity > StockQuantity)
            throw new DomainException($"Insufficient stock for product {Sku}: requested {quantity}, available {StockQuantity}");
        StockQuantity -= quantity;
        Touch();
    }

    public void Deactivate() { IsActive = false; Touch(); }
    public void Activate() { IsActive = true; Touch(); }

    public void SetDetail(string isbn, int pages, string language, int publishedYear, string? description)
    {
        if (Detail is null) Detail = new ProductDetail(Id, isbn, pages, language, publishedYear, description);
        else Detail.Update(isbn, pages, language, publishedYear, description);
        Touch();
    }

    public void AssignCategory(int categoryId, bool isPrimary = false)
    {
        if (_categories.Any(c => c.CategoryId == categoryId))
            throw new DomainException($"Product already belongs to category {categoryId}");
        if (isPrimary) foreach (var c in _categories) c.UnmarkPrimary();
        _categories.Add(new ProductCategory(Id, categoryId, isPrimary));
        Touch();
    }

    public void RemoveCategory(int categoryId)
    {
        var link = _categories.FirstOrDefault(c => c.CategoryId == categoryId)
                   ?? throw new DomainException($"Product does not belong to category {categoryId}");
        _categories.Remove(link);
        Touch();
    }

    private void Touch() => UpdatedAt = DateTime.UtcNow;
}
