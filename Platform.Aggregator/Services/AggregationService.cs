using System.Diagnostics;
using Platform.Aggregator.Clients;
using Platform.Aggregator.Models;

namespace Platform.Aggregator.Services;

/// <summary>Результат виклику downstream-сервісу: значення або попередження (часткова відмова).</summary>
public sealed record CallResult<T>(T? Value, string? Warning)
{
    public bool Failed => Warning is not null;
}

public sealed class AggregationService(
    OrdersClient orders,
    CatalogClient catalog,
    ReviewsClient reviews,
    ILogger<AggregationService> logger)
{
    /// <summary>«Картка замовлення»: замовлення обов'язкове; клієнт, товари та рейтинги — паралельно, з частковими відмовами.</summary>
    public async Task<OrderDetailsDto?> GetOrderDetailsAsync(long orderId, CancellationToken ct)
    {
        var orderResult = await CallAsync("orders-api", "GetOrder", () => orders.GetOrderAsync(orderId, ct));
        if (orderResult.Failed) throw new DownstreamUnavailableException("orders-api", orderResult.Warning!);
        var order = orderResult.Value;
        if (order is null) return null;

        var productIds = order.Items.Select(i => i.ProductId).Distinct().ToArray();

        // Усі незалежні виклики стартують одночасно й очікуються через Task.WhenAll
        var customerTask = CallAsync("orders-api", "GetCustomer", () => orders.GetCustomerAsync(order.CustomerId, ct));
        var productTasks = productIds.ToDictionary(id => id, id => CallAsync("catalog-api", $"GetProduct({id})", () => catalog.GetProductAsync(id, ct)));
        var ratingTasks = productIds.ToDictionary(id => id, id => CallAsync("reviews-api", $"GetRating({id})", () => reviews.GetRatingAsync(id, ct)));

        await Task.WhenAll(productTasks.Values.Cast<Task>().Concat(ratingTasks.Values).Append(customerTask));

        var warnings = new List<string>();
        var customer = await customerTask;
        if (customer.Warning is not null) warnings.Add(customer.Warning);
        if (productTasks.Values.Any(t => t.Result.Failed)) warnings.Add("Catalog is unavailable: current prices and stock are not shown");
        if (ratingTasks.Values.Any(t => t.Result.Failed)) warnings.Add("Reviews are unavailable: ratings are not shown");

        return new OrderDetailsDto
        {
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            Status = order.Status,
            Payable = order.Payable,
            CreatedAt = order.CreatedAt,
            Customer = customer.Value is { } c ? new CustomerSummaryDto(c.Id, c.FullName, c.ShippingCity, c.LoyaltyLevel) : null,
            Lines = order.Items.Select(i =>
            {
                var product = productTasks[i.ProductId].Result.Value;
                var rating = ratingTasks[i.ProductId].Result.Value;
                return new OrderLineDto
                {
                    ProductId = i.ProductId,
                    ProductName = i.ProductName,
                    UnitPrice = i.UnitPrice,
                    Quantity = i.Quantity,
                    CurrentPrice = product?.Price,
                    InStock = product is null ? null : product.StockQuantity > 0,
                    Rating = rating?.Average,
                    ReviewsCount = rating?.ReviewsCount
                };
            }).ToList(),
            Warnings = warnings
        };
    }

    /// <summary>«Картка книги»: Каталог обов'язковий, рейтинг і відгуки — опційні.</summary>
    public async Task<ProductCardDto?> GetProductCardAsync(long productId, CancellationToken ct)
    {
        var productTask = CallAsync("catalog-api", "GetProduct", () => catalog.GetProductAsync(productId, ct));
        var ratingTask = CallAsync("reviews-api", "GetRating", () => reviews.GetRatingAsync(productId, ct));
        var latestTask = CallAsync("reviews-api", "GetLatestReviews", () => reviews.GetLatestAsync(productId, 3, ct));
        await Task.WhenAll(productTask, ratingTask, latestTask);

        var product = await productTask;
        if (product.Failed) throw new DownstreamUnavailableException("catalog-api", product.Warning!);
        if (product.Value is not { } p) return null;

        var rating = await ratingTask;
        var latest = await latestTask;
        var warnings = new[] { rating.Warning, latest.Warning }.OfType<string>().Distinct().ToList();

        return new ProductCardDto
        {
            ProductId = p.Id,
            Name = p.Name,
            Author = p.Author,
            Price = p.Price,
            InStock = p.StockQuantity > 0,
            Publisher = p.PublisherName,
            Categories = p.Categories.Select(c => c.Name).ToList(),
            Rating = rating.Value is { } r ? new RatingSummaryDto(r.Average, r.ReviewsCount) : null,
            LatestReviews = latest.Value?.Items,
            Warnings = warnings
        };
    }

    /// <summary>Обгортка виклику: логування тривалості, перетворення збою на попередження замість 500.</summary>
    private async Task<CallResult<T>> CallAsync<T>(string service, string operation, Func<Task<T?>> call)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var value = await call();
            logger.LogInformation("Call {Service}.{Operation} succeeded in {ElapsedMs} ms", service, operation, sw.ElapsedMilliseconds);
            return new CallResult<T>(value, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TimeoutException
                                      or Polly.CircuitBreaker.BrokenCircuitException or Polly.Timeout.TimeoutRejectedException)
        {
            logger.LogWarning("Call {Service}.{Operation} failed in {ElapsedMs} ms: {Error}", service, operation, sw.ElapsedMilliseconds, ex.Message);
            return new CallResult<T>(default, $"{service} is unavailable ({operation})");
        }
    }
}

public sealed class DownstreamUnavailableException(string service, string message) : Exception(message)
{
    public string Service { get; } = service;
}
