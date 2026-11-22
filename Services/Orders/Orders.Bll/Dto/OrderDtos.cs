using System.ComponentModel.DataAnnotations;

namespace Orders.Bll.Dto;

// DTO — лише контракти API, без логіки. Перший рівень валідації — DataAnnotations.

public sealed class CreateOrderItemDto
{
    [Range(1, long.MaxValue)]
    public long ProductId { get; set; }

    [Range(1, 100, ErrorMessage = "Quantity must be between 1 and 100")]
    public int Quantity { get; set; }
}

public sealed class CreateOrderDto
{
    [Range(1, long.MaxValue)]
    public long CustomerId { get; set; }

    [Required, MinLength(1, ErrorMessage = "Order must contain at least one item"), MaxLength(50)]
    public List<CreateOrderItemDto> Items { get; set; } = [];

    [StringLength(500)]
    public string? Comment { get; set; }
}

public sealed class AddOrderItemDto
{
    [Range(1, long.MaxValue)]
    public long ProductId { get; set; }

    [Range(1, 100)]
    public int Quantity { get; set; }
}

public sealed class ChangeOrderStatusDto
{
    [Required, RegularExpression("^(Confirmed|Paid|Shipped|Completed|Cancelled)$",
        ErrorMessage = "Status must be one of Confirmed, Paid, Shipped, Completed, Cancelled")]
    public string Status { get; set; } = string.Empty;

    [StringLength(300)]
    public string? Reason { get; set; }

    /// <summary>Base64 rowversion для оптимістичної конкурентності (необов'язково).</summary>
    public string? RowVersion { get; set; }
}

public sealed class UpdateOrderCommentDto
{
    [StringLength(500)]
    public string? Comment { get; set; }

    [Required]
    public string RowVersion { get; set; } = string.Empty;
}

public sealed record OrderItemDto(long Id, long ProductId, string ProductName, decimal UnitPrice, int Quantity, decimal LineTotal);

public sealed class OrderDto
{
    public long Id { get; init; }
    public string OrderNumber { get; init; } = string.Empty;
    public long CustomerId { get; init; }
    public string Status { get; init; } = string.Empty;
    public decimal TotalAmount { get; init; }
    public decimal DiscountAmount { get; init; }
    public decimal Payable { get; init; }
    public string? Comment { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public string RowVersion { get; init; } = string.Empty;
    public List<OrderItemDto> Items { get; init; } = [];
}

public sealed record OrderStatusHistoryDto(string? FromStatus, string ToStatus, DateTime ChangedAt, string ChangedBy, string? Reason);
