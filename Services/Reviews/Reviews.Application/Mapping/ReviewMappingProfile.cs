using AutoMapper;
using MongoDB.Bson;
using Reviews.Application.Common;
using Reviews.Domain.Entities;
using Reviews.Domain.ValueObjects;

namespace Reviews.Application.Mapping;

/// <summary>Domain → DTO: ObjectId → string, VO → примітиви, вкладені масиви коментарів.</summary>
public sealed class ReviewMappingProfile : Profile
{
    public ReviewMappingProfile()
    {
        CreateMap<ObjectId, string>().ConvertUsing(id => id.ToString());
        CreateMap<AuthorInfo, AuthorDto>();
        CreateMap<Comment, CommentDto>();
        CreateMap<Review, ReviewDto>()
            .ForMember(d => d.ProductId, o => o.MapFrom(s => s.Product.ProductId))
            .ForMember(d => d.ProductName, o => o.MapFrom(s => s.Product.ProductName))
            .ForMember(d => d.Rating, o => o.MapFrom(s => s.Rating.Value));
    }
}
