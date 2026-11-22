using Microsoft.Extensions.DependencyInjection;
using Orders.Dal.Interfaces;

namespace Orders.Dal;

public static class DependencyInjection
{
    /// <summary>Реєстрація DAL: Unit of Work (Scoped — одне з'єднання на запит) і репозиторії з нього.</summary>
    public static IServiceCollection AddOrdersDal(this IServiceCollection services)
    {
        services.AddScoped<UnitOfWork.UnitOfWork>();
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<UnitOfWork.UnitOfWork>());
        services.AddScoped<IDbSession>(sp => sp.GetRequiredService<UnitOfWork.UnitOfWork>());
        services.AddScoped<IOrderRepository>(sp => sp.GetRequiredService<IUnitOfWork>().Orders);
        services.AddScoped<ICustomerRepository>(sp => sp.GetRequiredService<IUnitOfWork>().Customers);
        services.AddScoped<IProductDirectoryRepository>(sp => sp.GetRequiredService<IUnitOfWork>().Products);
        services.AddTransient<DbInitializer>();
        return services;
    }
}
