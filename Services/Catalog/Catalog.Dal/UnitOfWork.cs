using Catalog.Dal.Repositories;
using Catalog.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Catalog.Dal;

public interface IUnitOfWork
{
    IProductRepository Products { get; }
    IPublisherRepository Publishers { get; }
    ICategoryRepository Categories { get; }

    /// <summary>Єдина точка збереження всіх змін сценарію.</summary>
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

public sealed class UnitOfWork(
    CatalogDbContext context,
    IProductRepository products,
    IPublisherRepository publishers,
    ICategoryRepository categories) : IUnitOfWork
{
    public IProductRepository Products { get; } = products;
    public IPublisherRepository Publishers { get; } = publishers;
    public ICategoryRepository Categories { get; } = categories;

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            return await context.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException("The record was modified by another user");
        }
    }
}
