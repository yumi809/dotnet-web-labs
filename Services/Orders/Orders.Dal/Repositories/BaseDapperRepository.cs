using Dapper;
using Orders.Dal.Interfaces;

namespace Orders.Dal.Repositories;

/// <summary>
/// Спільна база Dapper-репозиторіїв: CRUD написано один раз; конкретні репозиторії задають
/// таблицю, список колонок і SQL вставки/оновлення та додають власні запити.
/// Усі запити параметризовані; з'єднання й транзакцію надає Unit of Work.
/// </summary>
public abstract class BaseDapperRepository<T>(IDbSession session) : IGenericRepository<T> where T : class
{
    protected IDbSession Session { get; } = session;

    /// <summary>Назва таблиці (константа в коді, не вхідні дані користувача).</summary>
    protected abstract string TableName { get; }
    /// <summary>Колонки для SELECT.</summary>
    protected abstract string SelectColumns { get; }
    protected abstract string InsertSql { get; }
    protected abstract string UpdateSql { get; }
    /// <summary>Чи підтримує таблиця м'яке видалення (IsDeleted).</summary>
    protected virtual bool SoftDelete => true;

    private string NotDeleted => SoftDelete ? " AND IsDeleted = 0" : string.Empty;

    protected CommandDefinition Command(string sql, object? param, CancellationToken ct) =>
        new(sql, param, Session.Transaction, commandTimeout: 30, cancellationToken: ct);

    public virtual async Task<T?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        var connection = await Session.GetOpenConnectionAsync(ct);
        return await connection.QuerySingleOrDefaultAsync<T>(
            Command($"SELECT {SelectColumns} FROM {TableName} WHERE Id = @Id{NotDeleted}", new { Id = id }, ct));
    }

    public virtual async Task<IReadOnlyList<T>> GetAllAsync(CancellationToken ct = default)
    {
        var connection = await Session.GetOpenConnectionAsync(ct);
        var where = SoftDelete ? " WHERE IsDeleted = 0" : string.Empty;
        var rows = await connection.QueryAsync<T>(Command($"SELECT {SelectColumns} FROM {TableName}{where}", null, ct));
        return rows.AsList();
    }

    public virtual async Task<long> AddAsync(T entity, CancellationToken ct = default)
    {
        var connection = await Session.GetOpenConnectionAsync(ct);
        return await connection.ExecuteScalarAsync<long>(Command(InsertSql + "; SELECT CAST(SCOPE_IDENTITY() AS BIGINT);", entity, ct));
    }

    public virtual async Task<bool> UpdateAsync(T entity, CancellationToken ct = default)
    {
        var connection = await Session.GetOpenConnectionAsync(ct);
        return await connection.ExecuteAsync(Command(UpdateSql, entity, ct)) > 0;
    }

    public virtual async Task<bool> DeleteAsync(long id, CancellationToken ct = default)
    {
        var connection = await Session.GetOpenConnectionAsync(ct);
        var sql = SoftDelete
            ? $"UPDATE {TableName} SET IsDeleted = 1, UpdatedAt = SYSUTCDATETIME() WHERE Id = @Id AND IsDeleted = 0"
            : $"DELETE FROM {TableName} WHERE Id = @Id";
        return await connection.ExecuteAsync(Command(sql, new { Id = id }, ct)) > 0;
    }
}
