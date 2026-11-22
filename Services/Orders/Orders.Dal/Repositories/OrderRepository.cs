using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using Orders.Dal.Interfaces;
using Orders.Domain.Exceptions;
using Orders.Domain.Models;

namespace Orders.Dal.Repositories;

/// <summary>Dapper-репозиторій замовлень: multi-mapping, збережені процедури, асинхронні методи.</summary>
public sealed class OrderRepository(IDbSession session) : BaseDapperRepository<Order>(session), IOrderRepository
{
    protected override string TableName => "dbo.Orders";

    protected override string SelectColumns =>
        "Id, OrderNumber, CustomerId, Status, TotalAmount, DiscountAmount, Comment, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy, IsDeleted, RowVersion";

    protected override string InsertSql =>
        """
        INSERT INTO dbo.Orders (OrderNumber, CustomerId, Status, TotalAmount, DiscountAmount, Comment, CreatedBy)
        VALUES (@OrderNumber, @CustomerId, @Status, @TotalAmount, @DiscountAmount, @Comment, @CreatedBy)
        """;

    // Оптимістична конкурентність: оновлюємо лише рядок з очікуваною rowversion
    protected override string UpdateSql =>
        """
        UPDATE dbo.Orders
           SET Comment = @Comment, UpdatedAt = SYSUTCDATETIME(), UpdatedBy = @UpdatedBy
         WHERE Id = @Id AND RowVersion = @RowVersion AND IsDeleted = 0
        """;

    /// <summary>Multi-mapping Order + OrderItem одним запитом (LEFT JOIN) з групуванням позицій.</summary>
    public async Task<Order?> GetWithItemsAsync(long orderId, CancellationToken ct = default)
    {
        const string sql =
            """
            SELECT o.Id, o.OrderNumber, o.CustomerId, o.Status, o.TotalAmount, o.DiscountAmount, o.Comment,
                   o.CreatedAt, o.CreatedBy, o.UpdatedAt, o.UpdatedBy, o.IsDeleted, o.RowVersion,
                   i.Id, i.OrderId, i.ProductId, i.ProductName, i.UnitPrice, i.Quantity, i.LineTotal
            FROM dbo.Orders o
            LEFT JOIN dbo.OrderItems i ON i.OrderId = o.Id
            WHERE o.Id = @OrderId AND o.IsDeleted = 0
            ORDER BY i.Id
            """;

        var connection = await Session.GetOpenConnectionAsync(ct);
        Order? result = null;
        await connection.QueryAsync<Order, OrderItem?, Order>(
            Command(sql, new { OrderId = orderId }, ct),
            (order, item) =>
            {
                result ??= order;
                if (item is not null && item.Id != 0) result.Items.Add(item);
                return result;
            },
            splitOn: "Id");
        return result;
    }

    public async Task<IReadOnlyList<Order>> GetByCustomerAsync(long customerId, CancellationToken ct = default)
    {
        var connection = await Session.GetOpenConnectionAsync(ct);
        var rows = await connection.QueryAsync<Order>(Command(
            $"SELECT {SelectColumns} FROM dbo.Orders WHERE CustomerId = @CustomerId AND IsDeleted = 0 ORDER BY CreatedAt DESC",
            new { CustomerId = customerId }, ct));
        return rows.AsList();
    }

    public async Task AddItemAsync(OrderItem item, CancellationToken ct = default)
    {
        const string sql =
            """
            INSERT INTO dbo.OrderItems (OrderId, ProductId, ProductName, UnitPrice, Quantity)
            VALUES (@OrderId, @ProductId, @ProductName, @UnitPrice, @Quantity)
            """;
        var connection = await Session.GetOpenConnectionAsync(ct);
        await connection.ExecuteAsync(Command(sql, item, ct));
    }

    public async Task RecalculateTotalAsync(long orderId, CancellationToken ct = default)
    {
        const string sql =
            """
            UPDATE dbo.Orders
               SET TotalAmount = ISNULL((SELECT SUM(LineTotal) FROM dbo.OrderItems WHERE OrderId = @OrderId), 0),
                   UpdatedAt = SYSUTCDATETIME()
             WHERE Id = @OrderId
            """;
        var connection = await Session.GetOpenConnectionAsync(ct);
        await connection.ExecuteAsync(Command(sql, new { OrderId = orderId }, ct));
    }

    /// <summary>Виклик збереженої процедури usp_Order_ChangeStatus; керовані помилки THROW 504xx → доменні винятки.</summary>
    public async Task ChangeStatusAsync(long orderId, string newStatus, string changedBy, string? reason, byte[]? rowVersion, CancellationToken ct = default)
    {
        var parameters = new DynamicParameters();
        parameters.Add("@OrderId", orderId, DbType.Int64);
        parameters.Add("@NewStatus", newStatus, DbType.String, size: 20);
        parameters.Add("@ChangedBy", changedBy, DbType.String, size: 100);
        parameters.Add("@Reason", reason, DbType.String, size: 300);
        parameters.Add("@RowVersion", rowVersion, DbType.Binary, size: 8);

        var connection = await Session.GetOpenConnectionAsync(ct);
        try
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "dbo.usp_Order_ChangeStatus", parameters, Session.Transaction,
                commandType: CommandType.StoredProcedure, cancellationToken: ct));
        }
        catch (SqlException ex) when (ex.Number == 50404)
        {
            throw new NotFoundException(nameof(Order), orderId);
        }
        catch (SqlException ex) when (ex.Number is 50409 or 50412)
        {
            throw new BusinessConflictException(ex.Message, ex);
        }
    }

    public async Task<bool> SoftDeleteAsync(long orderId, string deletedBy, CancellationToken ct = default)
    {
        var connection = await Session.GetOpenConnectionAsync(ct);
        try
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "dbo.usp_Order_SoftDelete", new { OrderId = orderId, DeletedBy = deletedBy }, Session.Transaction,
                commandType: CommandType.StoredProcedure, cancellationToken: ct));
            return true;
        }
        catch (SqlException ex) when (ex.Number == 50409)
        {
            throw new BusinessConflictException(ex.Message, ex);
        }
    }
}
