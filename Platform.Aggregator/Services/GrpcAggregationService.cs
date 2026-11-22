using System.Diagnostics;
using System.Text.Json;
using Catalog.Grpc.V1;
using Grpc.Core;
using Microsoft.Extensions.Caching.Distributed;
using Orders.Grpc.V1;
using Platform.Aggregator.Models;
using Reviews.Grpc.V1;

namespace Platform.Aggregator.Services;

/// <summary>
/// Агрегація через gRPC: паралельні виклики (Task.WhenAll) з дедлайнами; RpcException Unavailable/DeadlineExceeded
/// перетворюється на попередження. Повна (не часткова) відповідь кешується в Redis з коротким TTL.
/// </summary>
public sealed class GrpcAggregationService(
    OrdersService.OrdersServiceClient orders,
    CatalogService.CatalogServiceClient catalog,
    ReviewsService.ReviewsServiceClient reviews,
    IDistributedCache cache,
    ILogger<GrpcAggregationService> logger)
{
    private static readonly TimeSpan CallDeadline = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan AggregateTtl = TimeSpan.FromSeconds(45);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<ProductCardDto?> GetProductCardAsync(long productId, CancellationToken ct)
    {
        var cacheKey = $"aggregator:product-card:{productId}";
        if (await TryGetCachedAsync<ProductCardDto>(cacheKey, ct) is { } cached) return cached; // ДО gRPC-викликів

        var productTask = CallAsync("catalog-api", "GetProduct",
            () => catalog.GetProductAsync(new GetProductRequest { Id = productId }, deadline: Deadline(), cancellationToken: ct).ResponseAsync);
        var ratingTask = CallAsync("reviews-api", "GetProductRating",
            () => reviews.GetProductRatingAsync(new ProductRatingRequest { ProductId = productId }, deadline: Deadline(), cancellationToken: ct).ResponseAsync);
        var latestTask = CallAsync("reviews-api", "GetLatestReviews",
            () => reviews.GetLatestReviewsAsync(new LatestReviewsRequest { ProductId = productId, Count = 3 }, deadline: Deadline(), cancellationToken: ct).ResponseAsync);

        await Task.WhenAll(productTask, ratingTask, latestTask);

        var product = await productTask;
        if (product.Failed) throw new DownstreamUnavailableException("catalog-api", product.Warning!);
        if (product.Value is not { } p) return null;

        var rating = await ratingTask;
        var latest = await latestTask;
        var card = new ProductCardDto
        {
            ProductId = p.Id,
            Name = p.Name,
            Author = p.Author,
            Price = p.PriceCents / 100m,
            InStock = p.StockQuantity > 0,
            Publisher = p.PublisherName,
            Categories = p.Categories.Select(c => c.Name).ToList(),
            Rating = rating.Value is { } r ? new RatingSummaryDto(r.Average, r.ReviewsCount) : null,
            LatestReviews = latest.Value?.Reviews.Select(x => new ReviewItemResponse(x.Id, x.AuthorName, x.Rating, x.Title, x.HelpfulVotes, x.CreatedAt.ToDateTime())).ToList(),
            Warnings = new[] { rating.Warning, latest.Warning }.OfType<string>().Distinct().ToList()
        };

        await CacheIfCompleteAsync(cacheKey, card, card.Warnings.Count == 0, ct);
        return card;
    }

    public async Task<OrderDetailsDto?> GetOrderDetailsAsync(long orderId, CancellationToken ct)
    {
        var cacheKey = $"aggregator:order-details:{orderId}";
        if (await TryGetCachedAsync<OrderDetailsDto>(cacheKey, ct) is { } cached) return cached;

        var orderResult = await CallAsync("orders-api", "GetOrder",
            () => orders.GetOrderAsync(new GetOrderRequest { Id = orderId }, deadline: Deadline(), cancellationToken: ct).ResponseAsync);
        if (orderResult.Failed) throw new DownstreamUnavailableException("orders-api", orderResult.Warning!);
        if (orderResult.Value is not { } order) return null;

        var ids = order.Items.Select(i => i.ProductId).Distinct().ToArray();
        var customerTask = CallAsync("orders-api", "GetCustomer",
            () => orders.GetCustomerAsync(new GetCustomerRequest { Id = order.CustomerId }, deadline: Deadline(), cancellationToken: ct).ResponseAsync);
        var productsTask = CallAsync("catalog-api", "GetProducts", () =>
        {
            var request = new GetProductsRequest();
            request.Ids.AddRange(ids);
            return catalog.GetProductsAsync(request, deadline: Deadline(), cancellationToken: ct).ResponseAsync;
        });
        var ratingTasks = ids.ToDictionary(id => id, id => CallAsync("reviews-api", $"GetProductRating({id})",
            () => reviews.GetProductRatingAsync(new ProductRatingRequest { ProductId = id }, deadline: Deadline(), cancellationToken: ct).ResponseAsync));

        await Task.WhenAll(ratingTasks.Values.Cast<Task>().Append(customerTask).Append(productsTask));

        var customer = await customerTask;
        var products = await productsTask;
        var warnings = new List<string>();
        if (customer.Warning is not null) warnings.Add(customer.Warning);
        if (products.Warning is not null) warnings.Add("Catalog is unavailable: current prices and stock are not shown");
        if (ratingTasks.Values.Any(t => t.Result.Failed)) warnings.Add("Reviews are unavailable: ratings are not shown");

        var productMap = products.Value?.Products.ToDictionary(x => x.Id) ?? [];
        var details = new OrderDetailsDto
        {
            OrderId = order.Id,
            OrderNumber = order.OrderNumber,
            Status = order.Status,
            Payable = order.PayableCents / 100m,
            CreatedAt = order.CreatedAt.ToDateTime(),
            Customer = customer.Value is { } c ? new CustomerSummaryDto(c.Id, c.FullName, c.City, c.LoyaltyLevel) : null,
            Lines = order.Items.Select(i =>
            {
                productMap.TryGetValue(i.ProductId, out var p);
                var rating = ratingTasks[i.ProductId].Result.Value;
                return new OrderLineDto
                {
                    ProductId = i.ProductId,
                    ProductName = i.ProductName,
                    UnitPrice = i.UnitPriceCents / 100m,
                    Quantity = i.Quantity,
                    CurrentPrice = p is null ? null : p.PriceCents / 100m,
                    InStock = p is null ? null : p.StockQuantity > 0,
                    Rating = rating?.Average,
                    ReviewsCount = rating?.ReviewsCount
                };
            }).ToList(),
            Warnings = warnings
        };

        await CacheIfCompleteAsync(cacheKey, details, warnings.Count == 0, ct);
        return details;
    }

    private static DateTime Deadline() => DateTime.UtcNow.Add(CallDeadline);

    private async Task<T?> TryGetCachedAsync<T>(string key, CancellationToken ct) where T : class
    {
        var bytes = await cache.GetAsync(key, ct);
        if (bytes is null) return null;
        logger.LogDebug("Aggregate cache hit {CacheKey}", key);
        return JsonSerializer.Deserialize<T>(bytes, Json);
    }

    /// <summary>Часткові відповіді (з попередженнями) не кешуються.</summary>
    private async Task CacheIfCompleteAsync<T>(string key, T value, bool complete, CancellationToken ct)
    {
        if (!complete)
        {
            logger.LogInformation("Partial aggregate {CacheKey} is not cached", key);
            return;
        }
        await cache.SetAsync(key, JsonSerializer.SerializeToUtf8Bytes(value, Json),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = AggregateTtl }, ct);
    }

    private async Task<CallResult<T>> CallAsync<T>(string service, string operation, Func<Task<T>> call) where T : class
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var value = await call();
            logger.LogInformation("gRPC {Service}.{Operation} succeeded in {ElapsedMs} ms", service, operation, sw.ElapsedMilliseconds);
            return new CallResult<T>(value, null);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            return new CallResult<T>(null, null);
        }
        catch (RpcException ex) when (ex.StatusCode is StatusCode.Unavailable or StatusCode.DeadlineExceeded or StatusCode.ResourceExhausted or StatusCode.Internal)
        {
            logger.LogWarning("gRPC {Service}.{Operation} failed with {GrpcStatus} in {ElapsedMs} ms", service, operation, ex.StatusCode, sw.ElapsedMilliseconds);
            return new CallResult<T>(null, $"{service} is unavailable ({operation}: {ex.StatusCode})");
        }
    }
}
