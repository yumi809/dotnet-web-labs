using MediatR;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using Reviews.Application.Abstractions;
using Reviews.Application.Common;
using Reviews.Domain.Entities;
using Reviews.Domain.Interfaces;
using Reviews.Domain.ValueObjects;

namespace Reviews.Application.Commands;

// ---------- Create ----------
public sealed record CreateReviewCommand(
    long ProductId, string ProductName, string AuthorId, string AuthorName, int Rating, string Type,
    string Title, string? Body, List<string>? Pros, List<string>? Cons, string? VideoUrl) : ICommand<string>;

public sealed class CreateReviewHandler(IReviewRepository repository, ILogger<CreateReviewHandler> logger)
    : IRequestHandler<CreateReviewCommand, string>
{
    public async Task<string> Handle(CreateReviewCommand c, CancellationToken ct)
    {
        var review = new Review(
            new ProductRef(c.ProductId, c.ProductName),
            new AuthorInfo(c.AuthorId, c.AuthorName),
            new Rating(c.Rating), c.Type, c.Title, c.Body, c.Pros, c.Cons, c.VideoUrl);

        await repository.AddAsync(review, ct);
        logger.LogInformation("Review {ReviewId} created for product {ProductId} with rating {Rating}", review.Id, c.ProductId, c.Rating);
        return review.Id.ToString();
    }
}

// ---------- Update ----------
public sealed record UpdateReviewCommand(
    string Id, string RequestedBy, int Rating, string Title, string? Body,
    List<string>? Pros, List<string>? Cons, string? VideoUrl, int ExpectedVersion) : ICommand;

public sealed class UpdateReviewHandler(IReviewRepository repository, ILogger<UpdateReviewHandler> logger)
    : IRequestHandler<UpdateReviewCommand>
{
    public async Task Handle(UpdateReviewCommand c, CancellationToken ct)
    {
        var id = ObjectId.Parse(c.Id);
        var review = await repository.GetByIdAsync(id, ct) ?? throw new NotFoundException(nameof(Review), c.Id);
        if (review.Author.UserId != c.RequestedBy) throw new ConflictException("Only the author can edit the review");
        if (review.Version != c.ExpectedVersion)
            throw new ConflictException($"Review was modified (expected version {c.ExpectedVersion}, actual {review.Version})");

        review.Edit(new Rating(c.Rating), c.Title, c.Body, c.Pros, c.Cons, c.VideoUrl);
        await repository.UpdateAsync(review, c.ExpectedVersion, ct);
        logger.LogInformation("Review {ReviewId} updated to version {Version}", c.Id, review.Version);
    }
}

// ---------- Delete ----------
public sealed record DeleteReviewCommand(string Id) : ICommand;

public sealed class DeleteReviewHandler(IReviewRepository repository, ILogger<DeleteReviewHandler> logger)
    : IRequestHandler<DeleteReviewCommand>
{
    public async Task Handle(DeleteReviewCommand c, CancellationToken ct)
    {
        if (!await repository.DeleteAsync(ObjectId.Parse(c.Id), ct)) throw new NotFoundException(nameof(Review), c.Id);
        logger.LogInformation("Review {ReviewId} deleted", c.Id);
    }
}

// ---------- Add comment ----------
public sealed record AddCommentCommand(string ReviewId, string AuthorId, string AuthorName, string Text) : ICommand<string>;

public sealed class AddCommentHandler(IReviewRepository repository) : IRequestHandler<AddCommentCommand, string>
{
    public async Task<string> Handle(AddCommentCommand c, CancellationToken ct)
    {
        var review = await repository.GetByIdAsync(ObjectId.Parse(c.ReviewId), ct) ?? throw new NotFoundException(nameof(Review), c.ReviewId);
        var version = review.Version;
        var comment = review.AddComment(new AuthorInfo(c.AuthorId, c.AuthorName), c.Text);
        await repository.UpdateAsync(review, version, ct);
        return comment.Id.ToString();
    }
}

// ---------- Mark helpful ----------
public sealed record MarkReviewHelpfulCommand(string ReviewId) : ICommand;

public sealed class MarkReviewHelpfulHandler(IReviewRepository repository) : IRequestHandler<MarkReviewHelpfulCommand>
{
    public async Task Handle(MarkReviewHelpfulCommand c, CancellationToken ct)
    {
        var review = await repository.GetByIdAsync(ObjectId.Parse(c.ReviewId), ct) ?? throw new NotFoundException(nameof(Review), c.ReviewId);
        var version = review.Version;
        review.MarkHelpful();
        await repository.UpdateAsync(review, version, ct);
    }
}
