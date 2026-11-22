using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Orders.Dal.Interfaces;
using Orders.Dal.Repositories;

namespace Orders.Dal.UnitOfWork;

/// <summary>
/// Реалізація Unit of Work. Відкриває одне <see cref="SqlConnection"/> на scope (HTTP-запит),
/// керує транзакцією з рівнем ізоляції і передає з'єднання/транзакцію всім репозиторіям.
/// </summary>
public sealed class UnitOfWork : IUnitOfWork, IDbSession
{
    public const string ConnectionStringName = "OrdersDb";

    private readonly string _connectionString;
    private readonly ILogger<UnitOfWork> _logger;
    private SqlConnection? _connection;
    private SqlTransaction? _transaction;
    private bool _disposed;

    public UnitOfWork(IConfiguration configuration, ILoggerFactory loggerFactory)
    {
        _connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is not configured");
        _logger = loggerFactory.CreateLogger<UnitOfWork>();

        Orders = new OrderRepository(this);
        Customers = new CustomerRepository(this);
        StatusHistory = new OrderStatusHistoryRepository(this);
        Products = new ProductDirectoryRepository(this, loggerFactory.CreateLogger<ProductDirectoryRepository>());
        ProcessedEvents = new ProcessedEventRepository(this);
    }

    public IProcessedEventRepository ProcessedEvents { get; }

    public IOrderRepository Orders { get; }
    public ICustomerRepository Customers { get; }
    public IOrderStatusHistoryRepository StatusHistory { get; }
    public IProductDirectoryRepository Products { get; }

    public SqlTransaction? Transaction => _transaction;
    public bool HasActiveTransaction => _transaction is not null;

    public async Task<SqlConnection> GetOpenConnectionAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_connection is null)
        {
            _connection = new SqlConnection(_connectionString);
        }
        if (_connection.State != ConnectionState.Open)
        {
            await _connection.OpenAsync(ct);
        }
        return _connection;
    }

    public async Task BeginTransactionAsync(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted, CancellationToken ct = default)
    {
        if (_transaction is not null)
            throw new InvalidOperationException("Transaction is already started");

        var connection = await GetOpenConnectionAsync(ct);
        _transaction = (SqlTransaction)await connection.BeginTransactionAsync(isolationLevel, ct);
        _logger.LogDebug("Transaction started with isolation level {IsolationLevel}", isolationLevel);
    }

    public async Task CommitAsync(CancellationToken ct = default)
    {
        if (_transaction is null) throw new InvalidOperationException("No active transaction");
        try
        {
            await _transaction.CommitAsync(ct);
            _logger.LogDebug("Transaction committed");
        }
        finally
        {
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }

    public async Task RollbackAsync(CancellationToken ct = default)
    {
        if (_transaction is null) return;
        try
        {
            await _transaction.RollbackAsync(ct);
            _logger.LogWarning("Transaction rolled back");
        }
        finally
        {
            await _transaction.DisposeAsync();
            _transaction = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        if (_transaction is not null)
        {
            // незавершена транзакція відкочується автоматично
            await _transaction.RollbackAsync();
            await _transaction.DisposeAsync();
        }
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }
    }
}
