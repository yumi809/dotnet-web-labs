using FluentValidation;
using MediatR;
using MongoDB.Bson;
using Reviews.Application.Abstractions;
using Reviews.Application.Common;
using Reviews.Application.Interfaces;

namespace Reviews.Application.Queries;

// ---------- GetById ----------
public sealed record GetReviewByIdQuery(string Id) : IQuery<ReviewDto>;

public sealed class GetReviewByIdHandler(IReviewQueries queries) : IRequestHandler<GetReviewByIdQuery, ReviewDto>
{
    public async Task<ReviewDto> Handle(GetReviewByIdQuery q, CancellationToken ct) =>
        await queries.GetByIdAsync(ObjectId.Parse(q.Id), ct) ?? throw new NotFoundException("Review", q.Id);
}

public sealed class GetReviewByIdValidator : AbstractValidator<GetReviewByIdQuery>
{
    public GetReviewByIdValidator() =>
        RuleFor(x => x.Id).NotEmpty().Must(id => ObjectId.TryParse(id, out _)).WithMessage("'Id' must be a valid ObjectId");
}

// ---------- GetList ----------
public sealed record GetReviewsListQuery(
    long? ProductId, int? MinRating, string? AuthorId, string? Search,
    int Page = 1, int PageSize = 10, string SortBy = "newest") : IQuery<PagedList<ReviewListItemDto>>;

public sealed class GetReviewsListHandler(IReviewQueries queries) : IRequestHandler<GetReviewsListQuery, PagedList<ReviewListItemDto>>
{
    public Task<PagedList<ReviewListItemDto>> Handle(GetReviewsListQuery q, CancellationToken ct) =>
        queries.GetListAsync(new ReviewListFilter(q.ProductId, q.MinRating, q.AuthorId, q.Search, q.Page, q.PageSize, q.SortBy), ct);
}

public sealed class GetReviewsListValidator : AbstractValidator<GetReviewsListQuery>
{
    private static readonly string[] Sorts = ["newest", "oldest", "rating", "helpful"];

    public GetReviewsListValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 50);
        RuleFor(x => x.MinRating).InclusiveBetween(1, 5).When(x => x.MinRating.HasValue);
        RuleFor(x => x.ProductId).GreaterThan(0).When(x => x.ProductId.HasValue);
        RuleFor(x => x.Search).MaximumLength(100);
        RuleFor(x => x.SortBy).Must(s => Sorts.Contains(s)).WithMessage($"SortBy must be one of: {string.Join(", ", Sorts)}");
    }
}

// ---------- Product rating ----------
public sealed record GetProductRatingQuery(long ProductId) : IQuery<ProductRatingDto>;

public sealed class GetProductRatingHandler(IReviewQueries queries) : IRequestHandler<GetProductRatingQuery, ProductRatingDto>
{
    public async Task<ProductRatingDto> Handle(GetProductRatingQuery q, CancellationToken ct) =>
        await queries.GetProductRatingAsync(q.ProductId, ct) ?? throw new NotFoundException("ProductRating", q.ProductId);
}
