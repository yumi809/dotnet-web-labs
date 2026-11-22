using System.ComponentModel.DataAnnotations;

namespace Catalog.Bll.Dto;

public sealed record CategoryRefDto(int Id, string Name, string Slug, bool IsPrimary, DateTime AddedAt);

public sealed record ProductDetailDto(string Isbn, int Pages, string Language, int PublishedYear, string? Description);

public sealed class ProductListItemDto
{
    public long Id { get; init; }
    public string Sku { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Author { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public int StockQuantity { get; init; }
    public string? PublisherName { get; init; }
    public bool IsActive { get; init; }
}

public sealed class ProductDto
{
    public long Id { get; init; }
    public string Sku { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Author { get; init; } = string.Empty;
    public decimal Price { get; init; }
    public int StockQuantity { get; init; }
    public bool IsActive { get; init; }
    public int PublisherId { get; init; }
    public string? PublisherName { get; init; }
    public ProductDetailDto? Detail { get; init; }
    public List<CategoryRefDto> Categories { get; init; } = [];
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
}

// Вхідні DTO: DataAnnotations — перший рівень; FluentValidation-валідатори — другий (Validators/)

public sealed class CreateProductDto
{
    [Required, StringLength(32)] public string Sku { get; set; } = string.Empty;
    [Required, StringLength(200)] public string Name { get; set; } = string.Empty;
    [Required, StringLength(200)] public string Author { get; set; } = string.Empty;
    [Range(0.01, 1_000_000)] public decimal Price { get; set; }
    [Range(0, 100_000)] public int StockQuantity { get; set; }
    [Range(1, int.MaxValue)] public int PublisherId { get; set; }
    public List<int> CategoryIds { get; set; } = [];
    public CreateProductDetailDto? Detail { get; set; }
}

public sealed class CreateProductDetailDto
{
    [Required, StringLength(17)] public string Isbn { get; set; } = string.Empty;
    [Range(1, 10_000)] public int Pages { get; set; }
    [Required, StringLength(30)] public string Language { get; set; } = "uk";
    [Range(1450, 2100)] public int PublishedYear { get; set; }
    [StringLength(2000)] public string? Description { get; set; }
}

public sealed class UpdateProductDto
{
    [Required, StringLength(200)] public string Name { get; set; } = string.Empty;
    [Required, StringLength(200)] public string Author { get; set; } = string.Empty;
    [Range(0.01, 1_000_000)] public decimal Price { get; set; }
    [Range(1, int.MaxValue)] public int PublisherId { get; set; }
}

public sealed class StockChangeDto
{
    [Range(1, 10_000)] public int Quantity { get; set; }
}

/// <summary>Параметри списку: пагінація, фільтри, сортування (лише за дозволеними полями).</summary>
public sealed class ProductQueryDto
{
    public const int MaxPageSize = 50;

    [Range(1, int.MaxValue)] public int Page { get; set; } = 1;
    [Range(1, MaxPageSize)] public int PageSize { get; set; } = 10;
    [StringLength(100)] public string? Search { get; set; }
    public int? CategoryId { get; set; }
    public int? PublisherId { get; set; }
    [Range(0, 1_000_000)] public decimal? MinPrice { get; set; }
    [Range(0, 1_000_000)] public decimal? MaxPrice { get; set; }
    public bool? InStock { get; set; }
    /// <summary>name | price | createdAt | stock</summary>
    public string SortBy { get; set; } = "name";
    /// <summary>asc | desc</summary>
    public string SortDir { get; set; } = "asc";
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasNext => Page < TotalPages;
    public bool HasPrevious => Page > 1;
}

public sealed record PublisherDto(int Id, string Name, string Country, string? Website);
public sealed record PublisherWithProductsDto(int Id, string Name, string Country, IReadOnlyList<ProductListItemDto> Products);

public sealed class CreatePublisherDto
{
    [Required, StringLength(150)] public string Name { get; set; } = string.Empty;
    [Required, StringLength(80)] public string Country { get; set; } = string.Empty;
    [Url, StringLength(200)] public string? Website { get; set; }
}

public sealed record CategoryDto(int Id, string Name, string Slug);
public sealed record CategoryProductDto(long ProductId, string ProductName, decimal Price, DateTime AddedAt, bool IsPrimary);
