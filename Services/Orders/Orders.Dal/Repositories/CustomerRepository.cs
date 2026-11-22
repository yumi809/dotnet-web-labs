using System.Data;
using Dapper;
using Orders.Dal.Interfaces;
using Orders.Domain.Models;

namespace Orders.Dal.Repositories;

/// <summary>Dapper-репозиторій клієнтів: multi-mapping 1:1 Customer + CustomerProfile, виклик процедури статистики.</summary>
public sealed class CustomerRepository(IDbSession session) : BaseDapperRepository<Customer>(session), ICustomerRepository
{
    protected override string TableName => "dbo.Customers";

    protected override string SelectColumns =>
        "Id, Email, FullName, Phone, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy, IsDeleted, RowVersion";

    protected override string InsertSql =>
        "INSERT INTO dbo.Customers (Email, FullName, Phone, CreatedBy) VALUES (@Email, @FullName, @Phone, @CreatedBy)";

    protected override string UpdateSql =>
        """
        UPDATE dbo.Customers
           SET FullName = @FullName, Phone = @Phone, UpdatedAt = SYSUTCDATETIME(), UpdatedBy = @UpdatedBy
         WHERE Id = @Id AND RowVersion = @RowVersion AND IsDeleted = 0
        """;

    public async Task<Customer?> GetWithProfileAsync(long id, CancellationToken ct = default)
    {
        const string sql =
            """
            SELECT c.Id, c.Email, c.FullName, c.Phone, c.CreatedAt, c.CreatedBy, c.UpdatedAt, c.UpdatedBy, c.IsDeleted, c.RowVersion,
                   p.CustomerId, p.ShippingCity, p.ShippingAddress, p.PostalCode, p.LoyaltyLevel
            FROM dbo.Customers c
            LEFT JOIN dbo.CustomerProfiles p ON p.CustomerId = c.Id
            WHERE c.Id = @Id AND c.IsDeleted = 0
            """;
        var connection = await Session.GetOpenConnectionAsync(ct);
        var rows = await connection.QueryAsync<Customer, CustomerProfile?, Customer>(
            Command(sql, new { Id = id }, ct),
            (customer, profile) =>
            {
                customer.Profile = profile is { CustomerId: > 0 } ? profile : null;
                return customer;
            },
            splitOn: "CustomerId");
        return rows.FirstOrDefault();
    }

    public async Task<bool> EmailExistsAsync(string email, CancellationToken ct = default)
    {
        var connection = await Session.GetOpenConnectionAsync(ct);
        return await connection.ExecuteScalarAsync<bool>(Command(
            "SELECT CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.Customers WHERE Email = @Email) THEN 1 ELSE 0 END AS BIT)",
            new { Email = email }, ct));
    }

    public async Task<CustomerOrderStats?> GetOrderStatsAsync(long customerId, CancellationToken ct = default)
    {
        var connection = await Session.GetOpenConnectionAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<CustomerOrderStats>(new CommandDefinition(
            "dbo.usp_Customer_GetOrderStats", new { CustomerId = customerId }, Session.Transaction,
            commandType: CommandType.StoredProcedure, cancellationToken: ct));
    }
}
