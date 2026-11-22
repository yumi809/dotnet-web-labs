using Catalog.Dal.Specifications;
using Microsoft.EntityFrameworkCore;

namespace Catalog.Dal.Repositories;

public interface IGenericRepository<T> where T : class
{
    Task<T?> GetByIdAsync(object id, CancellationToken ct = default);
    Task<IReadOnlyList<T>> GetAllAsync(CancellationToken ct = default);
    Task AddAsync(T entity, CancellationToken ct = default);
    void Update(T entity);
    void Delete(T entity);
    Task<IReadOnlyList<T>> ListAsync(ISpecification<T> spec, CancellationToken ct = default);
    Task<T?> FirstOrDefaultAsync(ISpecification<T> spec, CancellationToken ct = default);
    Task<int> CountAsync(ISpecification<T> spec, CancellationToken ct = default);
}

/// <summary>
/// Асинхронний Generic Repository поверх DbSet. Update/Delete лише змінюють стан у ChangeTracker —
/// збереження виконує Unit of Work одним SaveChangesAsync.
/// </summary>
public class GenericRepository<T>(CatalogDbContext context) : IGenericRepository<T> where T : class
{
    protected CatalogDbContext Context { get; } = context;
    protected DbSet<T> Set { get; } = context.Set<T>();

    public virtual async Task<T?> GetByIdAsync(object id, CancellationToken ct = default) =>
        await Set.FindAsync([id], ct);

    public virtual async Task<IReadOnlyList<T>> GetAllAsync(CancellationToken ct = default) =>
        await Set.AsNoTracking().ToListAsync(ct);

    public virtual async Task AddAsync(T entity, CancellationToken ct = default) =>
        await Set.AddAsync(entity, ct);

    public virtual void Update(T entity) => Set.Update(entity);

    public virtual void Delete(T entity) => Set.Remove(entity);

    public async Task<IReadOnlyList<T>> ListAsync(ISpecification<T> spec, CancellationToken ct = default) =>
        await Set.WithSpecification(spec).ToListAsync(ct);   // Skip/Take застосовані ДО ToListAsync

    public async Task<T?> FirstOrDefaultAsync(ISpecification<T> spec, CancellationToken ct = default) =>
        await Set.WithSpecification(spec).FirstOrDefaultAsync(ct);

    public async Task<int> CountAsync(ISpecification<T> spec, CancellationToken ct = default) =>
        await Set.WithSpecification(spec, forCount: true).CountAsync(ct);
}
