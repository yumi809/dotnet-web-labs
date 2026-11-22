using MongoDB.Bson;
using Reviews.Domain.Entities;

namespace Reviews.Domain.Interfaces;

/// <summary>Контракт запису агрегату Review — оголошено в домені, реалізація в Infrastructure.</summary>
public interface IReviewRepository
{
    Task<Review?> GetByIdAsync(ObjectId id, CancellationToken ct = default);
    Task AddAsync(Review review, CancellationToken ct = default);
    /// <summary>Оновлює документ лише якщо його Version = expectedVersion; інакше — конфлікт.</summary>
    Task UpdateAsync(Review review, int expectedVersion, CancellationToken ct = default);
    Task<bool> DeleteAsync(ObjectId id, CancellationToken ct = default);
    /// <summary>Оновлює денормалізовану назву товару в усіх документах (Лаб. 08).</summary>
    Task<long> RenameProductAsync(long productId, string productName, CancellationToken ct = default);
}

public interface IDiscussionRepository
{
    Task<Discussion?> GetByIdAsync(ObjectId id, CancellationToken ct = default);
    Task AddAsync(Discussion discussion, CancellationToken ct = default);
    Task UpdateAsync(Discussion discussion, int expectedVersion, CancellationToken ct = default);
    Task AddMessageAsync(DiscussionMessage message, CancellationToken ct = default);
    Task<long> RenameProductAsync(long productId, string productName, CancellationToken ct = default);
}
