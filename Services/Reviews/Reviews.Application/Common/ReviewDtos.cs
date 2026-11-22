namespace Reviews.Application.Common;

public sealed record AuthorDto(string UserId, string DisplayName);

public sealed record CommentDto(string Id, AuthorDto Author, string Text, DateTime CreatedAt);

public sealed class ReviewDto
{
    public string Id { get; init; } = string.Empty;
    public long ProductId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public AuthorDto Author { get; init; } = null!;
    public int Rating { get; init; }
    public string Type { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string? Body { get; init; }
    public string? VideoUrl { get; init; }
    public List<string> Pros { get; init; } = [];
    public List<string> Cons { get; init; } = [];
    public int HelpfulVotes { get; init; }
    public List<CommentDto> Comments { get; init; } = [];
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public int Version { get; init; }
}

/// <summary>Легка проєкція для списку — без коментарів (проєкція виконується в MongoDB).</summary>
public sealed class ReviewListItemDto
{
    public string Id { get; init; } = string.Empty;
    public long ProductId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public string AuthorName { get; init; } = string.Empty;
    public int Rating { get; init; }
    public string Type { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public int CommentsCount { get; init; }
    public int HelpfulVotes { get; init; }
    public DateTime CreatedAt { get; init; }
}

public sealed record PagedList<T>(IReadOnlyList<T> Items, int Page, int PageSize, long TotalCount)
{
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasNext => Page < TotalPages;
    public bool HasPrevious => Page > 1;
}

public sealed record ProductRatingDto(long ProductId, string ProductName, long ReviewsCount, double Average);
