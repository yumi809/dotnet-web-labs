using AutoMapper;
using Catalog.Bll.Dto;
using Catalog.Dal;
using Catalog.Domain.Entities;
using Catalog.Domain.Exceptions;
using FluentValidation;

namespace Catalog.Bll.Services;

public interface IPublisherService
{
    Task<IReadOnlyList<PublisherDto>> GetAllAsync(CancellationToken ct);
    Task<PublisherWithProductsDto> GetWithProductsAsync(int id, CancellationToken ct);
    Task<PublisherDto> CreateAsync(CreatePublisherDto dto, CancellationToken ct);
}

public sealed class PublisherService(IUnitOfWork uow, IMapper mapper, IValidator<CreatePublisherDto> validator) : IPublisherService
{
    public async Task<IReadOnlyList<PublisherDto>> GetAllAsync(CancellationToken ct) =>
        mapper.Map<IReadOnlyList<PublisherDto>>(await uow.Publishers.GetAllAsync(ct));

    public async Task<PublisherWithProductsDto> GetWithProductsAsync(int id, CancellationToken ct)
    {
        var publisher = await uow.Publishers.GetWithProductsAsync(id, ct) ?? throw new NotFoundException(nameof(Publisher), id);
        return mapper.Map<PublisherWithProductsDto>(publisher);
    }

    public async Task<PublisherDto> CreateAsync(CreatePublisherDto dto, CancellationToken ct)
    {
        await validator.ValidateAndThrowAsync(dto, ct);
        if (await uow.Publishers.NameExistsAsync(dto.Name.Trim(), ct))
            throw new ConflictException($"Publisher '{dto.Name}' already exists");
        var publisher = new Publisher(dto.Name, dto.Country, dto.Website);
        await uow.Publishers.AddAsync(publisher, ct);
        await uow.SaveChangesAsync(ct);
        return mapper.Map<PublisherDto>(publisher);
    }
}

public interface ICategoryService
{
    Task<IReadOnlyList<CategoryDto>> GetAllAsync(CancellationToken ct);
    Task<IReadOnlyList<CategoryProductDto>> GetNewArrivalsAsync(int categoryId, int days, CancellationToken ct);
}

public sealed class CategoryService(IUnitOfWork uow, IMapper mapper) : ICategoryService
{
    public async Task<IReadOnlyList<CategoryDto>> GetAllAsync(CancellationToken ct) =>
        mapper.Map<IReadOnlyList<CategoryDto>>(await uow.Categories.GetAllAsync(ct));

    public async Task<IReadOnlyList<CategoryProductDto>> GetNewArrivalsAsync(int categoryId, int days, CancellationToken ct)
    {
        _ = await uow.Categories.GetByIdAsync(categoryId, ct) ?? throw new NotFoundException(nameof(Category), categoryId);
        var rows = await uow.Categories.GetProductsAddedSinceAsync(categoryId, DateTime.UtcNow.AddDays(-Math.Clamp(days, 1, 365)), ct);
        return mapper.Map<IReadOnlyList<CategoryProductDto>>(rows);
    }
}
