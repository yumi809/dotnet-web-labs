using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Catalog.Dal.Messaging;

/// <summary>
/// Запис про оброблену інтеграційну подію (ідемпотентність споживача). EventId — унікальний ключ;
/// зберігається в ТІЙ САМІЙ транзакції SaveChanges, що й бізнес-зміни.
/// </summary>
public sealed class ProcessedEvent
{
    public Guid EventId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}

public sealed class ProcessedEventConfiguration : IEntityTypeConfiguration<ProcessedEvent>
{
    public void Configure(EntityTypeBuilder<ProcessedEvent> builder)
    {
        builder.ToTable("ProcessedEvents");
        builder.HasKey(e => e.EventId);
        builder.Property(e => e.EventId).ValueGeneratedNever();
        builder.Property(e => e.EventType).HasMaxLength(200).IsRequired();
        builder.Property(e => e.ProcessedAt).HasDefaultValueSql("SYSUTCDATETIME()");
    }
}

public interface IProcessedEventRepository
{
    Task<bool> ExistsAsync(Guid eventId, CancellationToken ct = default);
    void Add(Guid eventId, string eventType);
}

public sealed class ProcessedEventRepository(CatalogDbContext context) : IProcessedEventRepository
{
    public Task<bool> ExistsAsync(Guid eventId, CancellationToken ct = default) =>
        context.Set<ProcessedEvent>().AnyAsync(e => e.EventId == eventId, ct);

    public void Add(Guid eventId, string eventType) =>
        context.Set<ProcessedEvent>().Add(new ProcessedEvent { EventId = eventId, EventType = eventType });
}
