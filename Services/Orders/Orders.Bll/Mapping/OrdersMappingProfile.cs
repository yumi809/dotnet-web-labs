using AutoMapper;
using Orders.Bll.Dto;
using Orders.Domain.Models;

namespace Orders.Bll.Mapping;

/// <summary>Усі мапінги сервісу Замовлень зібрані централізовано в BLL.</summary>
public sealed class OrdersMappingProfile : Profile
{
    public OrdersMappingProfile()
    {
        CreateMap<OrderItem, OrderItemDto>();
        CreateMap<Order, OrderDto>()
            .ForMember(d => d.RowVersion, o => o.MapFrom(s => Convert.ToBase64String(s.RowVersion)));
        CreateMap<OrderStatusHistory, OrderStatusHistoryDto>();

        CreateMap<CreateCustomerDto, Customer>()
            .ForMember(d => d.Email, o => o.MapFrom(s => s.Email.Trim().ToLowerInvariant()))
            .ForMember(d => d.Id, o => o.Ignore())
            .ForMember(d => d.Profile, o => o.Ignore())
            .ForMember(d => d.RowVersion, o => o.Ignore());
        CreateMap<Customer, CustomerDto>()
            .ForMember(d => d.ShippingCity, o => o.MapFrom(s => s.Profile != null ? s.Profile.ShippingCity : null))
            .ForMember(d => d.LoyaltyLevel, o => o.MapFrom(s => s.Profile != null ? s.Profile.LoyaltyLevel : null));
        CreateMap<CustomerOrderStats, CustomerStatsDto>();
        CreateMap<ProductInfo, ProductInfoDto>();
    }
}
