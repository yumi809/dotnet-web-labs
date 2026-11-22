using Orders.Bll.Services;

namespace Orders.Api.Metrics;

/// <summary>Знімок поточного стану для gauge-метрик (оновлюється у фоні, а не на кожен збір метрик).</summary>
public sealed class OrdersSnapshot
{
    public long AwaitingConfirmation { get; set; }
    public long AwaitingShipment { get; set; }
}

/// <summary>Раз на 30 с оновлює знімок легким запитом COUNT за статусом.</summary>
public sealed class OrdersSnapshotRefresher(IServiceScopeFactory scopeFactory, OrdersSnapshot snapshot, ILogger<OrdersSnapshotRefresher> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var orders = scope.ServiceProvider.GetRequiredService<IOrderService>();
                var counts = await orders.CountByStatusAsync(stoppingToken);
                snapshot.AwaitingConfirmation = counts.GetValueOrDefault("New");
                snapshot.AwaitingShipment = counts.GetValueOrDefault("Paid");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning("Orders snapshot refresh failed: {Error}", ex.Message);
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
