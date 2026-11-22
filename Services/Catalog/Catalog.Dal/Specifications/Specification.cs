using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace Catalog.Dal.Specifications;

/// <summary>Специфікація запиту: Where / Include / OrderBy / Skip / Take описуються окремо від репозиторію.</summary>
public interface ISpecification<T>
{
    IReadOnlyList<Expression<Func<T, bool>>> Criteria { get; }
    IReadOnlyList<Func<IQueryable<T>, IQueryable<T>>> Includes { get; }
    Func<IQueryable<T>, IOrderedQueryable<T>>? OrderBy { get; }
    int? Skip { get; }
    int? Take { get; }
    bool AsNoTracking { get; }
}

public abstract class Specification<T> : ISpecification<T> where T : class
{
    private readonly List<Expression<Func<T, bool>>> _criteria = [];
    private readonly List<Func<IQueryable<T>, IQueryable<T>>> _includes = [];

    public IReadOnlyList<Expression<Func<T, bool>>> Criteria => _criteria;
    public IReadOnlyList<Func<IQueryable<T>, IQueryable<T>>> Includes => _includes;
    public Func<IQueryable<T>, IOrderedQueryable<T>>? OrderBy { get; private set; }
    public int? Skip { get; private set; }
    public int? Take { get; private set; }
    public bool AsNoTracking { get; private set; } = true;

    protected void Where(Expression<Func<T, bool>> criteria) => _criteria.Add(criteria);
    protected void Include(Func<IQueryable<T>, IQueryable<T>> include) => _includes.Add(include);
    protected void ApplyOrderBy(Func<IQueryable<T>, IOrderedQueryable<T>> orderBy) => OrderBy = orderBy;
    protected void ApplyPaging(int skip, int take) { Skip = skip; Take = take; }
    protected void Tracked() => AsNoTracking = false;
}

/// <summary>Застосовує специфікацію до IQueryable — фільтрація, сортування та пагінація виконуються на рівні SQL.</summary>
public static class SpecificationEvaluator
{
    public static IQueryable<T> WithSpecification<T>(this IQueryable<T> query, ISpecification<T> spec, bool forCount = false)
        where T : class
    {
        if (spec.AsNoTracking) query = query.AsNoTracking();
        foreach (var criteria in spec.Criteria) query = query.Where(criteria);
        if (forCount) return query;

        foreach (var include in spec.Includes) query = include(query);
        if (spec.OrderBy is not null) query = spec.OrderBy(query);
        if (spec.Skip is not null) query = query.Skip(spec.Skip.Value);
        if (spec.Take is not null) query = query.Take(spec.Take.Value);
        return query;
    }
}
