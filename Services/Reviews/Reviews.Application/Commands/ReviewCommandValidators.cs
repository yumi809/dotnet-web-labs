using FluentValidation;
using MongoDB.Bson;
using Reviews.Application.Interfaces;
using Reviews.Domain.Entities;

namespace Reviews.Application.Commands;

internal static class ValidationRules
{
    public static IRuleBuilderOptions<T, string> MustBeObjectId<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().Must(id => ObjectId.TryParse(id, out _)).WithMessage("'{PropertyName}' must be a valid 24-character ObjectId");
}

public sealed class CreateReviewValidator : AbstractValidator<CreateReviewCommand>
{
    public CreateReviewValidator(IReviewQueries queries)
    {
        RuleFor(x => x.ProductId).GreaterThan(0);
        RuleFor(x => x.ProductName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.AuthorId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.AuthorName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Rating).InclusiveBetween(1, 5);
        RuleFor(x => x.Type).Must(t => ReviewTypes.All.Contains(t)).WithMessage($"Type must be one of: {string.Join(", ", ReviewTypes.All)}");
        RuleFor(x => x.Title).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Body).NotEmpty().When(x => x.Type == ReviewTypes.Text).MaximumLength(4000);
        RuleFor(x => x.VideoUrl).NotEmpty().Must(u => Uri.IsWellFormedUriString(u, UriKind.Absolute))
            .When(x => x.Type == ReviewTypes.Video).WithMessage("Video review requires an absolute videoUrl");

        // Асинхронне правило із запитом до MongoDB: один відгук на товар від користувача
        RuleFor(x => x)
            .MustAsync(async (cmd, ct) => !await queries.ExistsForAuthorAsync(cmd.ProductId, cmd.AuthorId, ct))
            .WithName("AuthorId")
            .WithMessage("User has already reviewed this product");
    }
}

public sealed class UpdateReviewValidator : AbstractValidator<UpdateReviewCommand>
{
    public UpdateReviewValidator()
    {
        RuleFor(x => x.Id).MustBeObjectId();
        RuleFor(x => x.RequestedBy).NotEmpty();
        RuleFor(x => x.Rating).InclusiveBetween(1, 5);
        RuleFor(x => x.Title).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Body).MaximumLength(4000);
        RuleFor(x => x.ExpectedVersion).GreaterThan(0);
    }
}

public sealed class DeleteReviewValidator : AbstractValidator<DeleteReviewCommand>
{
    public DeleteReviewValidator() => RuleFor(x => x.Id).MustBeObjectId();
}

public sealed class AddCommentValidator : AbstractValidator<AddCommentCommand>
{
    public AddCommentValidator()
    {
        RuleFor(x => x.ReviewId).MustBeObjectId();
        RuleFor(x => x.AuthorId).NotEmpty();
        RuleFor(x => x.AuthorName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Text).NotEmpty().MaximumLength(1000);
    }
}

public sealed class MarkReviewHelpfulValidator : AbstractValidator<MarkReviewHelpfulCommand>
{
    public MarkReviewHelpfulValidator() => RuleFor(x => x.ReviewId).MustBeObjectId();
}
