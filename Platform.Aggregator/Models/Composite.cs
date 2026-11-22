namespace Platform.Aggregator.Models;

/// <summary>
/// Композитний DTO бізнес-сценарію «Картка замовлення»: замовлення + клієнт + актуальний стан товарів
/// з Каталогу + рейтинг з Відгуків. Warnings — перелік недоступних частин (часткова відповідь).
/// </summary>
public sealed class OrderDetailsDto
{
    public required long OrderId { get; init; }
    public required string OrderNumber { get; init; }
    public required string Status { get; init; }
    public required decimal Payable { get; init; }
    public required DateTime CreatedAt { get; init; }
    public CustomerSummaryDto? Customer { get; init; }
    public required List<OrderLineDto> Lines { get; init; }
    public List<string> Warnings { get; init; } = [];
}

public sealed record CustomerSummaryDto(long Id, string FullName, string? City, string? LoyaltyLevel);

public sealed class OrderLineDto
{
    public required long ProductId { get; init; }
    public required string ProductName { get; init; }   // знімок із замовлення
    public required decimal UnitPrice { get; init; }     // знімок із замовлення
    public required int Quantity { get; init; }
    public decimal? CurrentPrice { get; init; }          // актуальна ціна з Каталогу (null — Каталог недоступний)
    public bool? InStock { get; init; }
    public double? Rating { get; init; }                 // з Відгуків
    public long? ReviewsCount { get; init; }
}

/// <summary>Композитний DTO «Картка книги»: Каталог + рейтинг + останні відгуки.</summary>
public sealed class ProductCardDto
{
    public required long ProductId { get; init; }
    public required string Name { get; init; }
    public required string Author { get; init; }
    public required decimal Price { get; init; }
    public required bool InStock { get; init; }
    public string? Publisher { get; init; }
    public List<string> Categories { get; init; } = [];
    public RatingSummaryDto? Rating { get; init; }
    public List<ReviewItemResponse>? LatestReviews { get; init; }
    public List<string> Warnings { get; init; } = [];
}

public sealed record RatingSummaryDto(double Average, long ReviewsCount);
