using MongoDB.Bson;

namespace Reviews.Infrastructure.Queries;

/// <summary>
/// Модель читання тієї ж колекції reviews (CQRS): прості властивості, які драйвер може
/// транслювати в $project. Доменний агрегат для читання не матеріалізується.
/// </summary>
internal sealed class ReviewReadModel
{
    public ObjectId Id { get; set; }
    public ProductRead Product { get; set; } = new();
    public AuthorRead Author { get; set; } = new();
    public RatingRead Rating { get; set; } = new();
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Body { get; set; }
    public string? VideoUrl { get; set; }
    public List<string> Pros { get; set; } = [];
    public List<string> Cons { get; set; } = [];
    public List<CommentRead> Comments { get; set; } = [];
    public int HelpfulVotes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int Version { get; set; }
}

internal sealed class ProductRead { public long ProductId { get; set; } public string ProductName { get; set; } = string.Empty; }
internal sealed class AuthorRead { public string UserId { get; set; } = string.Empty; public string DisplayName { get; set; } = string.Empty; }
internal sealed class RatingRead { public int Value { get; set; } }
internal sealed class CommentRead
{
    public ObjectId Id { get; set; }
    public AuthorRead Author { get; set; } = new();
    public string Text { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
