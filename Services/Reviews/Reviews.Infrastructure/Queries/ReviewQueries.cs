using MongoDB.Bson;
using MongoDB.Driver;
using Reviews.Application.Common;
using Reviews.Application.Interfaces;
using Reviews.Infrastructure.Persistence;

namespace Reviews.Infrastructure.Queries;

/// <summary>Читання: проєкція одразу в DTO на стороні MongoDB, фільтр $match, пагінація skip/limit.</summary>
public sealed class ReviewQueries(MongoDbContext context) : IReviewQueries
{
    private IMongoCollection<ReviewReadModel> Reviews =>
        context.Database.GetCollection<ReviewReadModel>(MongoDbContext.ReviewsCollection);

    public async Task<ReviewDto?> GetByIdAsync(ObjectId id, CancellationToken ct = default) =>
        await Reviews.Find(r => r.Id == id)
            .Project(r => new ReviewDto
            {
                Id = r.Id.ToString(),
                ProductId = r.Product.ProductId,
                ProductName = r.Product.ProductName,
                Author = new AuthorDto(r.Author.UserId, r.Author.DisplayName),
                Rating = r.Rating.Value,
                Type = r.Type,
                Title = r.Title,
                Body = r.Body,
                VideoUrl = r.VideoUrl,
                Pros = r.Pros,
                Cons = r.Cons,
                HelpfulVotes = r.HelpfulVotes,
                Comments = r.Comments.Select(c => new CommentDto(c.Id.ToString(), new AuthorDto(c.Author.UserId, c.Author.DisplayName), c.Text, c.CreatedAt)).ToList(),
                CreatedAt = r.CreatedAt,
                UpdatedAt = r.UpdatedAt,
                Version = r.Version
            })
            .FirstOrDefaultAsync(ct);

    public async Task<PagedList<ReviewListItemDto>> GetListAsync(ReviewListFilter f, CancellationToken ct = default)
    {
        var b = Builders<ReviewReadModel>.Filter;
        var filter = b.Empty;
        if (f.ProductId is { } productId) filter &= b.Eq(r => r.Product.ProductId, productId);
        if (f.MinRating is { } minRating) filter &= b.Gte(r => r.Rating.Value, minRating);
        if (!string.IsNullOrWhiteSpace(f.AuthorId)) filter &= b.Eq(r => r.Author.UserId, f.AuthorId);
        if (!string.IsNullOrWhiteSpace(f.Search)) filter &= b.Text(f.Search);

        var s = Builders<ReviewReadModel>.Sort;
        var sort = f.SortBy switch
        {
            "oldest" => s.Ascending(r => r.CreatedAt),
            "rating" => s.Descending(r => r.Rating.Value).Descending(r => r.CreatedAt),
            "helpful" => s.Descending(r => r.HelpfulVotes),
            _ => s.Descending(r => r.CreatedAt)
        };

        var countTask = Reviews.CountDocumentsAsync(filter, cancellationToken: ct);
        var itemsTask = Reviews.Find(filter)
            .Sort(sort)
            .Skip((f.Page - 1) * f.PageSize)
            .Limit(f.PageSize)
            .Project(r => new ReviewListItemDto
            {
                Id = r.Id.ToString(),
                ProductId = r.Product.ProductId,
                ProductName = r.Product.ProductName,
                AuthorName = r.Author.DisplayName,
                Rating = r.Rating.Value,
                Type = r.Type,
                Title = r.Title,
                CommentsCount = r.Comments.Count,
                HelpfulVotes = r.HelpfulVotes,
                CreatedAt = r.CreatedAt
            })
            .ToListAsync(ct);

        await Task.WhenAll(countTask, itemsTask);
        return new PagedList<ReviewListItemDto>(itemsTask.Result, f.Page, f.PageSize, countTask.Result);
    }

    public Task<bool> ExistsForAuthorAsync(long productId, string authorId, CancellationToken ct = default) =>
        Reviews.Find(r => r.Product.ProductId == productId && r.Author.UserId == authorId).AnyAsync(ct);

    public async Task<ProductRatingDto?> GetProductRatingAsync(long productId, CancellationToken ct = default)
    {
        var result = await Reviews.Aggregate()
            .Match(r => r.Product.ProductId == productId)
            .Group(r => r.Product.ProductId, g => new
            {
                ProductName = g.First().Product.ProductName,
                Count = g.Count(),
                Average = g.Average(r => r.Rating.Value)
            })
            .FirstOrDefaultAsync(ct);

        return result is null ? null : new ProductRatingDto(productId, result.ProductName, result.Count, Math.Round(result.Average, 2));
    }
}
