using MongoDB.Bson;
using MongoDB.Driver;
using Reviews.Application.Commands;

namespace Reviews.Infrastructure.Persistence;

/// <summary>Колекція processedEvents: _id = EventId (унікальний за визначенням), тип і час обробки.</summary>
public sealed class ProcessedEventStore(MongoDbContext context) : IProcessedEventStore
{
    private IMongoCollection<BsonDocument> Collection => context.Database.GetCollection<BsonDocument>("processedEvents");

    public async Task<bool> TryMarkProcessedAsync(Guid eventId, string eventType, CancellationToken ct)
    {
        try
        {
            await Collection.InsertOneAsync(new BsonDocument
            {
                ["_id"] = eventId.ToString(),
                ["eventType"] = eventType,
                ["processedAt"] = DateTime.UtcNow
            }, cancellationToken: ct);
            return true;
        }
        catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
        {
            return false;
        }
    }

    public Task UnmarkAsync(Guid eventId, CancellationToken ct) =>
        Collection.DeleteOneAsync(Builders<BsonDocument>.Filter.Eq("_id", eventId.ToString()), ct);
}
