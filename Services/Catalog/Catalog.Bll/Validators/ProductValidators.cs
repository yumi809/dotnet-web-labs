using Catalog.Bll.Dto;
using FluentValidation;

namespace Catalog.Bll.Validators;

public sealed class CreateProductValidator : AbstractValidator<CreateProductDto>
{
    public CreateProductValidator()
    {
        RuleFor(x => x.Sku).NotEmpty().Matches("^[A-Za-z]{2}-\\d{4,}$").WithMessage("SKU format: XX-0000");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Author).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Price).GreaterThan(0).PrecisionScale(18, 2, ignoreTrailingZeros: true);
        RuleFor(x => x.StockQuantity).GreaterThanOrEqualTo(0);
        RuleFor(x => x.PublisherId).GreaterThan(0);
        RuleFor(x => x.CategoryIds).NotEmpty().WithMessage("At least one category is required")
            .Must(ids => ids.Distinct().Count() == ids.Count).WithMessage("Categories must be unique");
        RuleFor(x => x.Detail!).SetValidator(new CreateProductDetailValidator()).When(x => x.Detail is not null);
    }
}

public sealed class CreateProductDetailValidator : AbstractValidator<CreateProductDetailDto>
{
    public CreateProductDetailValidator()
    {
        RuleFor(x => x.Isbn).NotEmpty()
            .Must(isbn => isbn.Replace("-", "") is { Length: 10 or 13 } digits && digits.All(char.IsDigit))
            .WithMessage("ISBN must contain 10 or 13 digits");
        RuleFor(x => x.Pages).InclusiveBetween(1, 10_000);
        RuleFor(x => x.Language).NotEmpty().Length(2, 30);
        RuleFor(x => x.PublishedYear).InclusiveBetween(1450, DateTime.UtcNow.Year + 1);
    }
}

public sealed class UpdateProductValidator : AbstractValidator<UpdateProductDto>
{
    public UpdateProductValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Author).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Price).GreaterThan(0).PrecisionScale(18, 2, ignoreTrailingZeros: true);
        RuleFor(x => x.PublisherId).GreaterThan(0);
    }
}

public sealed class ProductQueryValidator : AbstractValidator<ProductQueryDto>
{
    private static readonly string[] SortFields = ["name", "price", "createdat", "stock"];

    public ProductQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, ProductQueryDto.MaxPageSize);
        RuleFor(x => x.SortBy).Must(s => SortFields.Contains(s.ToLowerInvariant()))
            .WithMessage($"SortBy must be one of: {string.Join(", ", SortFields)}");
        RuleFor(x => x.SortDir).Must(s => s is "asc" or "desc").WithMessage("SortDir must be asc or desc");
        RuleFor(x => x).Must(x => x.MinPrice is null || x.MaxPrice is null || x.MinPrice <= x.MaxPrice)
            .WithName("MinPrice").WithMessage("MinPrice must be less than or equal to MaxPrice");
    }
}

public sealed class CreatePublisherValidator : AbstractValidator<CreatePublisherDto>
{
    public CreatePublisherValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Country).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Website).Must(w => Uri.IsWellFormedUriString(w, UriKind.Absolute)).When(x => x.Website is not null);
    }
}
