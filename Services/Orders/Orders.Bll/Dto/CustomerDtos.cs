using System.ComponentModel.DataAnnotations;

namespace Orders.Bll.Dto;

public sealed class CreateCustomerDto
{
    [Required, EmailAddress, StringLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required, StringLength(200, MinimumLength = 2)]
    public string FullName { get; set; } = string.Empty;

    [Phone, StringLength(32)]
    public string? Phone { get; set; }
}

public sealed class CustomerDto
{
    public long Id { get; init; }
    public string Email { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string? Phone { get; init; }
    public string? ShippingCity { get; init; }
    public string? LoyaltyLevel { get; init; }
    public DateTime CreatedAt { get; init; }
}

public sealed record CustomerStatsDto(long CustomerId, string FullName, int OrdersCount, decimal TotalSpent, DateTime? LastOrderAt);

public sealed record ProductInfoDto(long ProductId, string Name, decimal Price, bool IsAvailable);
