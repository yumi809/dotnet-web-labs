namespace Orders.Domain.Models;

/// <summary>Базові аудитні поля таблиць OrdersDb.</summary>
public abstract class AuditableEntity
{
    public long Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "system";
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
    /// <summary>SQL Server rowversion — оптимістична конкурентність.</summary>
    public byte[] RowVersion { get; set; } = [];
}

public class Customer : AuditableEntity
{
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Phone { get; set; }
    /// <summary>Зв'язок 1:1 (CustomerProfiles.CustomerId = PK і FK).</summary>
    public CustomerProfile? Profile { get; set; }
}

public class CustomerProfile
{
    public long CustomerId { get; set; }
    public string ShippingCity { get; set; } = string.Empty;
    public string ShippingAddress { get; set; } = string.Empty;
    public string? PostalCode { get; set; }
    public string LoyaltyLevel { get; set; } = "Bronze";
}

public class Order : AuditableEntity
{
    public string OrderNumber { get; set; } = string.Empty;
    public long CustomerId { get; set; }
    public string Status { get; set; } = OrderStatus.New;
    public decimal TotalAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public string? Comment { get; set; }
    public List<OrderItem> Items { get; set; } = [];

    public decimal Payable => TotalAmount - DiscountAmount;
}

public class OrderItem
{
    public long Id { get; set; }
    public long OrderId { get; set; }
    /// <summary>Логічне посилання на CatalogDb.Products.Id — без FOREIGN KEY між базами.</summary>
    public long ProductId { get; set; }
    /// <summary>Знімок назви на момент покупки.</summary>
    public string ProductName { get; set; } = string.Empty;
    /// <summary>Знімок ціни на момент покупки.</summary>
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public decimal LineTotal { get; set; }
}

public class OrderStatusHistory
{
    public long Id { get; set; }
    public long OrderId { get; set; }
    public string? FromStatus { get; set; }
    public string ToStatus { get; set; } = string.Empty;
    public DateTime ChangedAt { get; set; }
    public string ChangedBy { get; set; } = "system";
    public string? Reason { get; set; }
}

/// <summary>Локальна копія довідника товарів з Каталогу (оновлюється подіями).</summary>
public class ProductInfo
{
    public long ProductId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public bool IsAvailable { get; set; }
    public DateTime SyncedAt { get; set; }
}

public class CustomerOrderStats
{
    public long CustomerId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public int OrdersCount { get; set; }
    public decimal TotalSpent { get; set; }
    public DateTime? LastOrderAt { get; set; }
}

public static class OrderStatus
{
    public const string New = "New";
    public const string Confirmed = "Confirmed";
    public const string Paid = "Paid";
    public const string Shipped = "Shipped";
    public const string Completed = "Completed";
    public const string Cancelled = "Cancelled";

    public static readonly IReadOnlyCollection<string> All = [New, Confirmed, Paid, Shipped, Completed, Cancelled];

    private static readonly Dictionary<string, string[]> Transitions = new()
    {
        [New] = [Confirmed, Cancelled],
        [Confirmed] = [Paid, Cancelled],
        [Paid] = [Shipped, Cancelled],
        [Shipped] = [Completed],
        [Completed] = [],
        [Cancelled] = []
    };

    public static bool CanTransition(string from, string to) =>
        Transitions.TryGetValue(from, out var allowed) && allowed.Contains(to);
}
