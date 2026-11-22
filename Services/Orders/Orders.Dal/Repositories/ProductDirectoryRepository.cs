using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Orders.Dal.Interfaces;
using Orders.Domain.Models;

namespace Orders.Dal.Repositories;

/// <summary>
/// Репозиторій на ЧИСТОМУ ADO.NET: SqlCommand з параметрами, ручне читання SqlDataReader.
/// Свідомо не успадковує Generic Repository — довідник має природний ключ з Каталогу та операцію upsert.
/// </summary>
public sealed class ProductDirectoryRepository(IDbSession session, ILogger<ProductDirectoryRepository> logger)
    : IProductDirectoryRepository
{
    private const string SelectSql = "SELECT ProductId, Name, Price, IsAvailable, SyncedAt FROM dbo.Products";

    public async Task<ProductInfo?> GetAsync(long productId, CancellationToken ct = default)
    {
        var connection = await session.GetOpenConnectionAsync(ct);
        await using var command = new SqlCommand($"{SelectSql} WHERE ProductId = @ProductId", connection, session.Transaction);
        command.Parameters.Add("@ProductId", SqlDbType.BigInt).Value = productId;

        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, ct);
        return await reader.ReadAsync(ct) ? Map(reader) : null;
    }

    public async Task<IReadOnlyDictionary<long, ProductInfo>> GetManyAsync(IEnumerable<long> productIds, CancellationToken ct = default)
    {
        var ids = productIds.Distinct().ToArray();
        var result = new Dictionary<long, ProductInfo>();
        if (ids.Length == 0) return result;

        var connection = await session.GetOpenConnectionAsync(ct);
        await using var command = new SqlCommand { Connection = connection, Transaction = session.Transaction };
        // Параметр на кожен Id — без конкатенації вхідних даних у текст запиту
        var names = new string[ids.Length];
        for (var i = 0; i < ids.Length; i++)
        {
            names[i] = $"@p{i}";
            command.Parameters.Add(names[i], SqlDbType.BigInt).Value = ids[i];
        }
        command.CommandText = $"{SelectSql} WHERE ProductId IN ({string.Join(", ", names)})";

        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var product = Map(reader);
            result[product.ProductId] = product;
        }
        return result;
    }

    public async Task<IReadOnlyList<ProductInfo>> GetAllAsync(CancellationToken ct = default)
    {
        var connection = await session.GetOpenConnectionAsync(ct);
        await using var command = new SqlCommand($"{SelectSql} ORDER BY Name", connection, session.Transaction);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var list = new List<ProductInfo>();
        while (await reader.ReadAsync(ct)) list.Add(Map(reader));
        return list;
    }

    public async Task UpsertAsync(ProductInfo product, CancellationToken ct = default)
    {
        const string sql =
            """
            MERGE dbo.Products AS t
            USING (SELECT @ProductId AS ProductId) AS s ON t.ProductId = s.ProductId
            WHEN MATCHED THEN UPDATE SET Name = @Name, Price = @Price, IsAvailable = @IsAvailable, SyncedAt = SYSUTCDATETIME()
            WHEN NOT MATCHED THEN INSERT (ProductId, Name, Price, IsAvailable) VALUES (@ProductId, @Name, @Price, @IsAvailable);
            """;
        var connection = await session.GetOpenConnectionAsync(ct);
        await using var command = new SqlCommand(sql, connection, session.Transaction);
        command.Parameters.Add("@ProductId", SqlDbType.BigInt).Value = product.ProductId;
        command.Parameters.Add("@Name", SqlDbType.NVarChar, 200).Value = product.Name;
        command.Parameters.Add(new SqlParameter("@Price", SqlDbType.Decimal) { Precision = 18, Scale = 2, Value = product.Price });
        command.Parameters.Add("@IsAvailable", SqlDbType.Bit).Value = product.IsAvailable;
        var affected = await command.ExecuteNonQueryAsync(ct);
        logger.LogDebug("Product directory upsert {ProductId}: {Affected} row(s)", product.ProductId, affected);
    }

    private static ProductInfo Map(SqlDataReader reader) => new()
    {
        ProductId = reader.GetInt64(reader.GetOrdinal("ProductId")),
        Name = reader.GetString(reader.GetOrdinal("Name")),
        Price = reader.GetDecimal(reader.GetOrdinal("Price")),
        IsAvailable = reader.GetBoolean(reader.GetOrdinal("IsAvailable")),
        SyncedAt = reader.GetDateTime(reader.GetOrdinal("SyncedAt"))
    };
}
