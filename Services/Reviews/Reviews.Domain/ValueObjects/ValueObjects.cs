using Reviews.Domain.Common;

namespace Reviews.Domain.ValueObjects;

/// <summary>Оцінка 1..5 — незмінний VO.</summary>
public sealed class Rating : ValueObject
{
    public int Value { get; }

    public Rating(int value)
    {
        if (value is < 1 or > 5) throw new DomainException("Rating must be between 1 and 5");
        Value = value;
    }

    protected override IEnumerable<object?> GetEqualityComponents() { yield return Value; }
}

/// <summary>Автор відгуку/коментаря — знімок даних користувача.</summary>
public sealed class AuthorInfo : ValueObject
{
    public string UserId { get; }
    public string DisplayName { get; }

    public AuthorInfo(string userId, string displayName)
    {
        if (string.IsNullOrWhiteSpace(userId)) throw new DomainException("Author userId is required");
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Length > 100) throw new DomainException("Author display name must be 1..100 characters");
        UserId = userId;
        DisplayName = displayName.Trim();
    }

    protected override IEnumerable<object?> GetEqualityComponents() { yield return UserId; yield return DisplayName; }
}

/// <summary>Посилання на товар Каталогу + денормалізована назва (політика дублювання Лаб. 01).</summary>
public sealed class ProductRef : ValueObject
{
    public long ProductId { get; }
    public string ProductName { get; }

    public ProductRef(long productId, string productName)
    {
        if (productId <= 0) throw new DomainException("ProductId must be positive");
        if (string.IsNullOrWhiteSpace(productName)) throw new DomainException("ProductName is required");
        ProductId = productId;
        ProductName = productName;
    }

    public ProductRef WithName(string newName) => new(ProductId, newName);

    protected override IEnumerable<object?> GetEqualityComponents() { yield return ProductId; yield return ProductName; }
}
