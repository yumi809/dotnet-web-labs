using Orders.Domain.Models;

namespace Orders.Dal.Interfaces;

/// <summary>Узагальнений репозиторій: спільний CRUD. Повертає моделі, а не DataReader/DataTable.</summary>
public interface IGenericRepository<T> where T : class
{
    Task<T?> GetByIdAsync(long id, CancellationToken ct = default);
    Task<IReadOnlyList<T>> GetAllAsync(CancellationToken ct = default);
    Task<long> AddAsync(T entity, CancellationToken ct = default);
    Task<bool> UpdateAsync(T entity, CancellationToken ct = default);
    Task<bool> DeleteAsync(long id, CancellationToken ct = default);
}

/// <summary>Специфічні запити до замовлень (Dapper: multi-mapping, stored procedures).</summary>
public interface IOrderRepository : IGenericRepository<Order>
{
    Task<Order?> GetWithItemsAsync(long orderId, CancellationToken ct = default);
    Task<IReadOnlyList<Order>> GetByCustomerAsync(long customerId, CancellationToken ct = default);
    Task AddItemAsync(OrderItem item, CancellationToken ct = default);
    Task RecalculateTotalAsync(long orderId, CancellationToken ct = default);
    Task ChangeStatusAsync(long orderId, string newStatus, string changedBy, string? reason, byte[]? rowVersion, CancellationToken ct = default);
    Task<bool> SoftDeleteAsync(long orderId, string deletedBy, CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, long>> CountByStatusAsync(CancellationToken ct = default);
}

public interface ICustomerRepository : IGenericRepository<Customer>
{
    Task<Customer?> GetWithProfileAsync(long id, CancellationToken ct = default);
    Task<bool> EmailExistsAsync(string email, CancellationToken ct = default);
    Task<CustomerOrderStats?> GetOrderStatsAsync(long customerId, CancellationToken ct = default);
}

public interface IOrderStatusHistoryRepository
{
    Task AddAsync(OrderStatusHistory entry, CancellationToken ct = default);
    Task<IReadOnlyList<OrderStatusHistory>> GetByOrderAsync(long orderId, CancellationToken ct = default);
}

/// <summary>
/// Локальний довідник товарів. Реалізація — на чистому ADO.NET і свідомо не успадковує Generic Repository:
/// ключ приходить із Каталогу, а операція запису — upsert, а не Insert/Update.
/// </summary>
public interface IProductDirectoryRepository
{
    Task<ProductInfo?> GetAsync(long productId, CancellationToken ct = default);
    Task<IReadOnlyDictionary<long, ProductInfo>> GetManyAsync(IEnumerable<long> productIds, CancellationToken ct = default);
    Task<IReadOnlyList<ProductInfo>> GetAllAsync(CancellationToken ct = default);
    Task UpsertAsync(ProductInfo product, CancellationToken ct = default);
}
