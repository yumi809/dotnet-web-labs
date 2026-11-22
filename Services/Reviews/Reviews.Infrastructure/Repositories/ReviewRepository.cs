using MongoDB.Bson;
using MongoDB.Driver;
using Reviews.Application.Common;
using Reviews.Domain.Entities;
using Reviews.Domain.Interfaces;
using Reviews.Infrastructure.Persistence;

namespace Reviews.Infrastructure.Repositories;

/// <summary>Репозиторій агрегату Review на MongoDB Driver з оптимістичною конкурентністю за полем version.</summary>
public sealed class ReviewRepository(MongoDbContext context) : IReviewRepository
{
    public async Task<Review?> GetByIdAsync(ObjectId id, CancellationToken ct = default) =>
        await context.Reviews.Find(r => r.Id == id).FirstOrDefaultAsync(ct);

    public Task AddAsync(Review review, CancellationToken ct = default) =>
        context.Reviews.InsertOneAsync(review, cancellationToken: ct);

    public async Task UpdateAsync(Review review, int expectedVersion, CancellationToken ct = default)
    {
        review.IncrementVersion();
        // Оновлення застосовується лише до документа з очікуваною версією
        var filter = Builders<Review>.Filter.Eq(r => r.Id, review.Id) & Builders<Review>.Filter.Eq(r => r.Version, expectedVersion);
        var result = await context.Reviews.ReplaceOneAsync(filter, review, cancellationToken: ct);
        if (result.MatchedCount == 0)
            throw new ConflictException($"Review {review.Id} was modified concurrently (expected version {expectedVersion})");
    }

    public async Task<bool> DeleteAsync(ObjectId id, CancellationToken ct = default) =>
        (await context.Reviews.DeleteOneAsync(r => r.Id == id, ct)).DeletedCount > 0;

    public async Task<long> RenameProductAsync(long productId, string productName, CancellationToken ct = default)
    {
        var result = await context.Reviews.UpdateManyAsync(
            Builders<Review>.Filter.Eq("product.productId", productId),
            Builders<Review>.Update.Set("product.productName", productName).Inc(r => r.Version, 1),
            cancellationToken: ct);
        return result.ModifiedCount;
    }
}

public sealed class DiscussionRepository(MongoDbContext context) : IDiscussionRepository
{
    public async Task<Discussion?> GetByIdAsync(ObjectId id, CancellationToken ct = default) =>
        await context.Discussions.Find(d => d.Id == id).FirstOrDefaultAsync(ct);

    public Task AddAsync(Discussion discussion, CancellationToken ct = default) =>
        context.Discussions.InsertOneAsync(discussion, cancellationToken: ct);

    public async Task UpdateAsync(Discussion discussion, int expectedVersion, CancellationToken ct = default)
    {
        discussion.IncrementVersion();
        var result = await context.Discussions.ReplaceOneAsync(
            d => d.Id == discussion.Id && d.Version == expectedVersion, discussion, cancellationToken: ct);
        if (result.MatchedCount == 0) throw new ConflictException($"Discussion {discussion.Id} was modified concurrently");
    }

    public Task AddMessageAsync(DiscussionMessage message, CancellationToken ct = default) =>
        context.Messages.InsertOneAsync(message, cancellationToken: ct);

    public async Task<long> RenameProductAsync(long productId, string productName, CancellationToken ct = default) =>
        (await context.Discussions.UpdateManyAsync(
            Builders<Discussion>.Filter.Eq("product.productId", productId),
            Builders<Discussion>.Update.Set("product.productName", productName),
            cancellationToken: ct)).ModifiedCount;
}
