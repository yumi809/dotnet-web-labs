using Microsoft.Extensions.DependencyInjection;
using Orders.Dal;

namespace Orders.Bll;

public static class DatabaseExtensions
{
    /// <summary>Розгортає OrdersDb ідемпотентними скриптами Lab01 (викликається з Api лише в Development).</summary>
    public static async Task InitializeOrdersDatabaseAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DbInitializer>().InitializeAsync(ct);
    }
}
