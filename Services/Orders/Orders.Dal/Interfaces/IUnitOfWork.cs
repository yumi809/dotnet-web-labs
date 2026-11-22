using System.Data;

namespace Orders.Dal.Interfaces;

/// <summary>
/// Unit of Work: одне з'єднання і одна транзакція на весь бізнес-сценарій.
/// Усі репозиторії отримують спільне з'єднання та поточну транзакцію через <see cref="IDbSession"/>.
/// </summary>
public interface IUnitOfWork : IAsyncDisposable
{
    IOrderRepository Orders { get; }
    ICustomerRepository Customers { get; }
    IOrderStatusHistoryRepository StatusHistory { get; }
    IProductDirectoryRepository Products { get; }

    bool HasActiveTransaction { get; }
    Task BeginTransactionAsync(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted, CancellationToken ct = default);
    Task CommitAsync(CancellationToken ct = default);
    Task RollbackAsync(CancellationToken ct = default);
}

/// <summary>Спільний контекст доступу до БД для репозиторіїв у межах одного UoW.</summary>
public interface IDbSession
{
    Task<Microsoft.Data.SqlClient.SqlConnection> GetOpenConnectionAsync(CancellationToken ct = default);
    Microsoft.Data.SqlClient.SqlTransaction? Transaction { get; }
}
