using AutoMapper;
using Catalog.Bll.Dto;
using Catalog.Dal.Repositories;
using Catalog.Domain.Entities;

namespace Catalog.Bll.Mapping;

public sealed class CatalogMappingProfile : Profile
{
    public CatalogMappingProfile()
    {
        CreateMap<ProductDetail, ProductDetailDto>();
        CreateMap<ProductCategory, CategoryRefDto>()
            .ForCtorParam(nameof(CategoryRefDto.Id), o => o.MapFrom(s => s.CategoryId))
            .ForCtorParam(nameof(CategoryRefDto.Name), o => o.MapFrom(s => s.Category.Name))
            .ForCtorParam(nameof(CategoryRefDto.Slug), o => o.MapFrom(s => s.Category.Slug));
        CreateMap<Product, ProductDto>()
            .ForMember(d => d.PublisherName, o => o.MapFrom(s => s.Publisher != null ? s.Publisher.Name : null));
        CreateMap<Product, ProductListItemDto>()
            .ForMember(d => d.PublisherName, o => o.MapFrom(s => s.Publisher != null ? s.Publisher.Name : null));
        CreateMap<Publisher, PublisherDto>();
        CreateMap<Publisher, PublisherWithProductsDto>();
        CreateMap<Category, CategoryDto>();
        CreateMap<CategoryProductRow, CategoryProductDto>();
    }
}
