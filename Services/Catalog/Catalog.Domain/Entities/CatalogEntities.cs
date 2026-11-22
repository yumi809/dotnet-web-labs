using Catalog.Domain.Exceptions;

namespace Catalog.Domain.Entities;

/// <summary>Видавництво — 1:N до книг.</summary>
public class Publisher
{
    private readonly List<Product> _products = [];

    public int Id { get; private set; }
    public string Name { get; private set; } = null!;
    public string Country { get; private set; } = null!;
    public string? Website { get; private set; }
    public IReadOnlyCollection<Product> Products => _products.AsReadOnly();

    private Publisher() { }

    public Publisher(string name, string country, string? website = null)
    {
        Update(name, country, website);
    }

    public void Update(string name, string country, string? website)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 150) throw new DomainException("Publisher name must be 1..150 characters");
        if (string.IsNullOrWhiteSpace(country)) throw new DomainException("Country is required");
        if (website is not null && !Uri.IsWellFormedUriString(website, UriKind.Absolute)) throw new DomainException("Website must be an absolute URL");
        Name = name.Trim();
        Country = country.Trim();
        Website = website;
    }
}

/// <summary>Категорія — довідник з фіксованими Id (HasData).</summary>
public class Category
{
    private readonly List<ProductCategory> _products = [];

    public int Id { get; private set; }
    public string Name { get; private set; } = null!;
    public string Slug { get; private set; } = null!;
    public IReadOnlyCollection<ProductCategory> Products => _products.AsReadOnly();

    private Category() { }

    public Category(int id, string name, string slug)
    {
        if (id <= 0) throw new DomainException("Category id must be positive");
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Category name is required");
        if (string.IsNullOrWhiteSpace(slug)) throw new DomainException("Category slug is required");
        Id = id;
        Name = name;
        Slug = slug.ToLowerInvariant();
    }
}

/// <summary>Проміжна сутність M:N зі складеним ключем і власними полями AddedAt / IsPrimary.</summary>
public class ProductCategory
{
    public long ProductId { get; private set; }
    public Product Product { get; private set; } = null!;
    public int CategoryId { get; private set; }
    public Category Category { get; private set; } = null!;
    public DateTime AddedAt { get; private set; }
    public bool IsPrimary { get; private set; }

    private ProductCategory() { }

    internal ProductCategory(long productId, int categoryId, bool isPrimary)
    {
        if (categoryId <= 0) throw new DomainException("Category is required");
        ProductId = productId;
        CategoryId = categoryId;
        IsPrimary = isPrimary;
        AddedAt = DateTime.UtcNow;
    }

    internal void UnmarkPrimary() => IsPrimary = false;
}

/// <summary>Деталі книги — 1:1 (ProductId — водночас PK і FK).</summary>
public class ProductDetail
{
    public long ProductId { get; private set; }
    public string Isbn { get; private set; } = null!;
    public int Pages { get; private set; }
    public string Language { get; private set; } = null!;
    public int PublishedYear { get; private set; }
    public string? Description { get; private set; }

    private ProductDetail() { }

    internal ProductDetail(long productId, string isbn, int pages, string language, int publishedYear, string? description)
    {
        ProductId = productId;
        Update(isbn, pages, language, publishedYear, description);
    }

    internal void Update(string isbn, int pages, string language, int publishedYear, string? description)
    {
        var digits = isbn.Replace("-", string.Empty);
        if (digits.Length is not (10 or 13) || !digits.All(char.IsDigit)) throw new DomainException("ISBN must contain 10 or 13 digits");
        if (pages <= 0) throw new DomainException("Pages must be positive");
        if (publishedYear < 1450 || publishedYear > DateTime.UtcNow.Year + 1) throw new DomainException("Published year is out of range");
        Isbn = isbn;
        Pages = pages;
        Language = language;
        PublishedYear = publishedYear;
        Description = description;
    }
}
