using MongoDB.Bson;
using Reviews.Application.Common;

namespace Reviews.Application.Interfaces;

public sealed record ReviewListFilter(long? ProductId, int? MinRating, string? AuthorId, string? Search, int Page, int PageSize, string SortBy);

/// <summary>
/// Сторона читання (CQRS): інтерфейс оголошено в Application, реалізація в Infrastructure
/// робить проєкцію одразу в DTO і пагінацію skip/limit у MongoDB.
/// </summary>
public interface IReviewQueries
{
    Task<ReviewDto?> GetByIdAsync(ObjectId id, CancellationToken ct = default);
    Task<PagedList<ReviewListItemDto>> GetListAsync(ReviewListFilter filter, CancellationToken ct = default);
    Task<bool> ExistsForAuthorAsync(long productId, string authorId, CancellationToken ct = default);
    Task<ProductRatingDto?> GetProductRatingAsync(long productId, CancellationToken ct = default);
}
