using Microsoft.Extensions.DependencyInjection;
using Orders.Bll.Mapping;
using Orders.Bll.Services;
using Orders.Dal;

namespace Orders.Bll;

public static class DependencyInjection
{
    public static IServiceCollection AddOrdersBll(this IServiceCollection services)
    {
        services.AddOrdersDal();
        services.AddAutoMapper(cfg => { }, typeof(OrdersMappingProfile).Assembly);
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<ICustomerService, CustomerService>();
        return services;
    }
}
