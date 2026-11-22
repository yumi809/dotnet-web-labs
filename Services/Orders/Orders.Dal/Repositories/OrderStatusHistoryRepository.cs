using Dapper;
using Orders.Dal.Interfaces;
using Orders.Domain.Models;

namespace Orders.Dal.Repositories;

/// <summary>Журнал статусів — append-only, тому без Generic Repository (немає Update/Delete).</summary>
public sealed class OrderStatusHistoryRepository(IDbSession session) : IOrderStatusHistoryRepository
{
    public async Task AddAsync(OrderStatusHistory entry, CancellationToken ct = default)
    {
        const string sql =
            """
            INSERT INTO dbo.OrderStatusHistory (OrderId, FromStatus, ToStatus, ChangedBy, Reason)
            VALUES (@OrderId, @FromStatus, @ToStatus, @ChangedBy, @Reason)
            """;
        var connection = await session.GetOpenConnectionAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(sql, entry, session.Transaction, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<OrderStatusHistory>> GetByOrderAsync(long orderId, CancellationToken ct = default)
    {
        const string sql =
            """
            SELECT Id, OrderId, FromStatus, ToStatus, ChangedAt, ChangedBy, Reason
            FROM dbo.OrderStatusHistory WHERE OrderId = @OrderId ORDER BY ChangedAt, Id
            """;
        var connection = await session.GetOpenConnectionAsync(ct);
        var rows = await connection.QueryAsync<OrderStatusHistory>(
            new CommandDefinition(sql, new { OrderId = orderId }, session.Transaction, cancellationToken: ct));
        return rows.AsList();
    }
}
