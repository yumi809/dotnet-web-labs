namespace Platform.Aggregator.Models;

// DTO відповідей мікросервісів (десеріалізація у типізованих клієнтах)

public sealed record OrderItemResponse(long Id, long ProductId, string ProductName, decimal UnitPrice, int Quantity, decimal LineTotal);

public sealed record OrderResponse(
    long Id, string OrderNumber, long CustomerId, string Status, decimal TotalAmount, decimal DiscountAmount,
    decimal Payable, DateTime CreatedAt, List<OrderItemResponse> Items);

public sealed record CustomerResponse(long Id, string Email, string FullName, string? ShippingCity, string? LoyaltyLevel);

public sealed record CategoryRefResponse(int Id, string Name);

public sealed record ProductResponse(
    long Id, string Sku, string Name, string Author, decimal Price, int StockQuantity, bool IsActive,
    string? PublisherName, List<CategoryRefResponse> Categories);

public sealed record RatingResponse(long ProductId, string ProductName, long ReviewsCount, double Average);

public sealed record ReviewItemResponse(string Id, string AuthorName, int Rating, string Title, int HelpfulVotes, DateTime CreatedAt);

public sealed record ReviewPageResponse(List<ReviewItemResponse> Items, long TotalCount);
