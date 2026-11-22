using Dapper;
using Microsoft.Data.SqlClient;
using Orders.Dal.Interfaces;

namespace Orders.Dal.Repositories;

/// <summary>
/// Таблиця оброблених подій (dbo.ProcessedEvents, PK = EventId). Запис виконується в ПОТОЧНІЙ транзакції UoW —
/// разом з бізнес-змінами; дубль EventId → порушення PK (2627) → подію вже оброблено.
/// </summary>
public sealed class ProcessedEventRepository(IDbSession session) : IProcessedEventRepository
{
    public async Task<bool> TryMarkProcessedAsync(Guid eventId, string eventType, CancellationToken ct = default)
    {
        const string sql = "INSERT INTO dbo.ProcessedEvents (EventId, EventType) VALUES (@EventId, @EventType)";
        var connection = await session.GetOpenConnectionAsync(ct);
        try
        {
            await connection.ExecuteAsync(new CommandDefinition(sql, new { EventId = eventId, EventType = eventType }, session.Transaction, cancellationToken: ct));
            return true;
        }
        catch (SqlException ex) when (ex.Number is 2627 or 2601)
        {
            return false;
        }
    }
}
