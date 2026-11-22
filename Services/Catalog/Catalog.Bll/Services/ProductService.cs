using AutoMapper;
using Catalog.Bll.Dto;
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
    ILogger<ProductService> logger) : IProductService
{
    public async Task<PagedResult<ProductListItemDto>> GetPagedAsync(ProductQueryDto query, CancellationToken ct)
    {
        await queryValidator.ValidateAndThrowAsync(query, ct);

        var total = await uow.Products.CountAsync(new ProductFilterSpecification(query, paged: false), ct);
        var items = await uow.Products.ListAsync(new ProductFilterSpecification(query), ct);
        return new PagedResult<ProductListItemDto>(mapper.Map<IReadOnlyList<ProductListItemDto>>(items), query.Page, query.PageSize, total);
    }

    public async Task<ProductDto> GetAsync(long id, CancellationToken ct)
    {
        var product = await uow.Products.GetWithDetailsAsync(id, ct) ?? throw new NotFoundException(nameof(Product), id);
        return mapper.Map<ProductDto>(product);
    }

    public async Task<ProductDto> CreateAsync(CreateProductDto dto, CancellationToken ct)
    {
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
        logger.LogInformation("Product {ProductId} {Sku} created", product.Id, product.Sku);
        return await GetAsync(product.Id, ct);
    }

    public async Task<ProductDto> UpdateAsync(long id, UpdateProductDto dto, CancellationToken ct)
    {
        await updateValidator.ValidateAndThrowAsync(dto, ct);
        var product = await uow.Products.GetByIdAsync(id, ct) ?? throw new NotFoundException(nameof(Product), id);
        if (product.PublisherId != dto.PublisherId)
            _ = await uow.Publishers.GetByIdAsync(dto.PublisherId, ct) ?? throw new NotFoundException(nameof(Publisher), dto.PublisherId);

        var oldPrice = product.Price;
        product.Rename(dto.Name, dto.Author);
        product.ChangePrice(dto.Price);
        product.ChangePublisher(dto.PublisherId);
        await uow.SaveChangesAsync(ct);
        logger.LogInformation("Product {ProductId} updated, price {OldPrice} -> {NewPrice}", id, oldPrice, dto.Price);
        return await GetAsync(id, ct);
    }

    public async Task<ProductDto> AddStockAsync(long id, int quantity, CancellationToken ct)
    {
        var product = await uow.Products.GetByIdAsync(id, ct) ?? throw new NotFoundException(nameof(Product), id);
        product.AddStock(quantity);
        await uow.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<ProductDto> RemoveStockAsync(long id, int quantity, CancellationToken ct)
    {
        var product = await uow.Products.GetByIdAsync(id, ct) ?? throw new NotFoundException(nameof(Product), id);
        product.RemoveStock(quantity);
        await uow.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task DeleteAsync(long id, CancellationToken ct)
    {
        var product = await uow.Products.GetByIdAsync(id, ct) ?? throw new NotFoundException(nameof(Product), id);
        product.Deactivate(); // м'яке видалення: книга лишається в історії замовлень
        await uow.SaveChangesAsync(ct);
        logger.LogInformation("Product {ProductId} deactivated", id);
    }

    public async Task<IReadOnlyList<ProductListItemDto>> GetLowStockAsync(int threshold, CancellationToken ct) =>
        mapper.Map<IReadOnlyList<ProductListItemDto>>(await uow.Products.ListAsync(new LowStockSpecification(threshold), ct));
}
