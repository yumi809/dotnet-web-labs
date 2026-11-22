using AutoMapper;
using Catalog.Bll.Caching;
using Catalog.Bll.Dto;
using Catalog.Bll.Events;
using Catalog.Bll.Specifications;
using Catalog.Dal;
using Catalog.Domain.Entities;
using Catalog.Domain.Exceptions;
using FluentValidation;
using Microsoft.Extensions.Logging;

namespace Catalog.Bll.Services;

public interface IProductService
{
    Task<PagedResult<ProductListItemDto>> GetPagedAsync(ProductQueryDto query, CancellationToken ct);
    Task<ProductDto> GetAsync(long id, CancellationToken ct);
    Task<ProductDto> CreateAsync(CreateProductDto dto, CancellationToken ct);
    Task<ProductDto> UpdateAsync(long id, UpdateProductDto dto, CancellationToken ct);
    Task<ProductDto> AddStockAsync(long id, int quantity, CancellationToken ct);
    Task<ProductDto> RemoveStockAsync(long id, int quantity, CancellationToken ct);
    Task DeleteAsync(long id, CancellationToken ct);
    Task<IReadOnlyList<ProductListItemDto>> GetLowStockAsync(int threshold, CancellationToken ct);
}

/// <summary>Сервіс каталогу працює лише через Unit of Work (без прямого доступу до DbContext).</summary>
public sealed class ProductService(
    IUnitOfWork uow,
    IMapper mapper,
    IValidator<CreateProductDto> createValidator,
    IValidator<UpdateProductDto> updateValidator,
    IValidator<ProductQueryDto> queryValidator,
    ICatalogCache cache,
    ICatalogMetrics metrics,
    IProductEventsPublisher events,
    ILogger<ProductService> logger) : IProductService
{
    /// <summary>
    /// Публікація ПІСЛЯ успішного SaveChanges. Збій брокера не відкочує вже збережені зміни — він логується
    /// з бізнес-контекстом (гарантовану доставку дав би Transactional Outbox).
    /// </summary>
    private async Task PublishChangedAsync(long productId, decimal? oldPrice, CancellationToken ct)
    {
        try
        {
            var product = await GetAsync(productId, ct);
            await events.ProductChangedAsync(product, oldPrice, ct);
        }
        catch (Exception ex) when (ex is not NotFoundException)
        {
            logger.LogError(ex, "Failed to publish ProductUpdated for product {ProductId}", productId);
        }
    }

    /// <summary>Тривалість операцій запису записується і для успішних, і для невдалих виконань.</summary>
    private async Task<T> MeasureAsync<T>(string operation, Func<Task<T>> action)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var result = await action();
            metrics.RecordDuration(operation, true, sw.Elapsed.TotalMilliseconds);
            return result;
        }
        catch
        {
            metrics.RecordDuration(operation, false, sw.Elapsed.TotalMilliseconds);
            throw;
        }
    }

    /// <summary>Сторінки каталогу — read-heavy: cache-aside з ключем із параметрів запиту та версії списків.</summary>
    public async Task<PagedResult<ProductListItemDto>> GetPagedAsync(ProductQueryDto query, CancellationToken ct)
    {
        await queryValidator.ValidateAndThrowAsync(query, ct);

        var version = await cache.GetListVersionAsync(ct);
        var key = CacheKeys.ProductList(version, QueryHash(query));
        return (await cache.GetOrCreateAsync(key, "product-list", CacheTtl.ProductList, async token =>
        {
            var total = await uow.Products.CountAsync(new ProductFilterSpecification(query, paged: false), token);
            var items = await uow.Products.ListAsync(new ProductFilterSpecification(query), token);
            return new PagedResult<ProductListItemDto>(mapper.Map<IReadOnlyList<ProductListItemDto>>(items), query.Page, query.PageSize, total);
        }, ct))!;
    }

    /// <summary>Картка товару: L1 → L2 → БД (ключ catalog:product:{id}).</summary>
    public async Task<ProductDto> GetAsync(long id, CancellationToken ct) =>
        await cache.GetOrCreateAsync(CacheKeys.Product(id), "product", CacheTtl.Product, async token =>
        {
            var product = await uow.Products.GetWithDetailsAsync(id, token);
            return product is null ? null : mapper.Map<ProductDto>(product);
        }, ct) ?? throw new NotFoundException(nameof(Product), id);

    /// <summary>Інвалідація одразу після запису: картка товару + усі сторінки списків.</summary>
    private async Task InvalidateAsync(long productId, string reason, CancellationToken ct)
    {
        await cache.RemoveAsync(CacheKeys.Product(productId), reason, ct);
        await cache.BumpListVersionAsync(reason, ct);
    }

    private static string QueryHash(ProductQueryDto q) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            $"{q.Page}|{q.PageSize}|{q.Search}|{q.CategoryId}|{q.PublisherId}|{q.MinPrice}|{q.MaxPrice}|{q.InStock}|{q.SortBy}|{q.SortDir}")))[..16];

    public async Task<ProductDto> CreateAsync(CreateProductDto dto, CancellationToken ct)
    {
        var created = await MeasureAsync("create", () => CreateCoreAsync(dto, ct));
        metrics.ProductCreated(created.Categories.FirstOrDefault(c => c.IsPrimary)?.Slug ?? "none");
        return created;
    }

    private async Task<ProductDto> CreateCoreAsync(CreateProductDto dto, CancellationToken ct)
    {
        using var activity = CatalogTelemetry.ActivitySource.StartActivity("catalog.product.create");
        activity?.SetTag("product.sku", dto.Sku);
        await createValidator.ValidateAndThrowAsync(dto, ct);

        // Правила, що потребують БД
        var sku = dto.Sku.Trim().ToUpperInvariant();
        if (await uow.Products.SkuExistsAsync(sku, ct: ct))
            throw new ConflictException($"Product with SKU '{sku}' already exists");
        _ = await uow.Publishers.GetByIdAsync(dto.PublisherId, ct) ?? throw new NotFoundException(nameof(Publisher), dto.PublisherId);
        foreach (var categoryId in dto.CategoryIds)
            _ = await uow.Categories.GetByIdAsync(categoryId, ct) ?? throw new NotFoundException(nameof(Category), categoryId);

        var product = new Product(sku, dto.Name, dto.Author, dto.Price, dto.StockQuantity, dto.PublisherId);
        if (dto.Detail is { } d) product.SetDetail(d.Isbn, d.Pages, d.Language, d.PublishedYear, d.Description);
        for (var i = 0; i < dto.CategoryIds.Count; i++) product.AssignCategory(dto.CategoryIds[i], isPrimary: i == 0);

        await uow.Products.AddAsync(product, ct);
        await uow.SaveChangesAsync(ct);
        await cache.BumpListVersionAsync("product created", ct);
        logger.LogInformation("Product {ProductId} {Sku} created", product.Id, product.Sku);
        await PublishChangedAsync(product.Id, null, ct);
        return await GetAsync(product.Id, ct);
    }

    public Task<ProductDto> UpdateAsync(long id, UpdateProductDto dto, CancellationToken ct) =>
        MeasureAsync("update", () => UpdateCoreAsync(id, dto, ct));

    private async Task<ProductDto> UpdateCoreAsync(long id, UpdateProductDto dto, CancellationToken ct)
    {
        using var activity = CatalogTelemetry.ActivitySource.StartActivity("catalog.product.update");
        activity?.SetTag("product.id", id);
        await updateValidator.ValidateAndThrowAsync(dto, ct);
        var product = await uow.Products.GetByIdAsync(id, ct) ?? throw new NotFoundException(nameof(Product), id);
        if (product.PublisherId != dto.PublisherId)
            _ = await uow.Publishers.GetByIdAsync(dto.PublisherId, ct) ?? throw new NotFoundException(nameof(Publisher), dto.PublisherId);

        var oldPrice = product.Price;
        product.Rename(dto.Name, dto.Author);
        product.ChangePrice(dto.Price);
        product.ChangePublisher(dto.PublisherId);
        await uow.SaveChangesAsync(ct);
        await InvalidateAsync(id, "product updated", ct);
        metrics.PriceChanged(oldPrice, dto.Price);
        await PublishChangedAsync(id, oldPrice, ct);
        logger.LogInformation("Product {ProductId} updated, price {OldPrice} -> {NewPrice}", id, oldPrice, dto.Price);
        return await GetAsync(id, ct);
    }

    public async Task<ProductDto> AddStockAsync(long id, int quantity, CancellationToken ct)
    {
        var product = await uow.Products.GetByIdAsync(id, ct) ?? throw new NotFoundException(nameof(Product), id);
        product.AddStock(quantity);
        await uow.SaveChangesAsync(ct);
        await InvalidateAsync(id, "stock added", ct);
        metrics.StockMoved("add", quantity);
        return await GetAsync(id, ct);
    }

    public async Task<ProductDto> RemoveStockAsync(long id, int quantity, CancellationToken ct)
    {
        var product = await uow.Products.GetByIdAsync(id, ct) ?? throw new NotFoundException(nameof(Product), id);
        product.RemoveStock(quantity);
        await uow.SaveChangesAsync(ct);
        await InvalidateAsync(id, "stock removed", ct);
        metrics.StockMoved("remove", quantity);
        return await GetAsync(id, ct);
    }

    public async Task DeleteAsync(long id, CancellationToken ct)
    {
        var product = await uow.Products.GetByIdAsync(id, ct) ?? throw new NotFoundException(nameof(Product), id);
        product.Deactivate(); // м'яке видалення: книга лишається в історії замовлень
        await uow.SaveChangesAsync(ct);
        await InvalidateAsync(id, "product deleted", ct);
        await PublishChangedAsync(id, null, ct);   // IsActive = false → у довіднику Замовлень товар стане недоступним
        logger.LogInformation("Product {ProductId} deactivated", id);
    }

    public async Task<IReadOnlyList<ProductListItemDto>> GetLowStockAsync(int threshold, CancellationToken ct) =>
        mapper.Map<IReadOnlyList<ProductListItemDto>>(await uow.Products.ListAsync(new LowStockSpecification(threshold), ct));
}
