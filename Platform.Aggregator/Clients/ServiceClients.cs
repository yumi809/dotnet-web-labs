using System.Net;
using Platform.Aggregator.Models;

namespace Platform.Aggregator.Clients;

/// <summary>Typed HttpClient сервісу Замовлень. BaseAddress = https+http://orders-api (service discovery).</summary>
public sealed class OrdersClient(HttpClient http)
{
    public Task<OrderResponse?> GetOrderAsync(long id, CancellationToken ct) => http.GetJsonOrNullAsync<OrderResponse>($"api/orders/{id}", ct);
    public Task<CustomerResponse?> GetCustomerAsync(long id, CancellationToken ct) => http.GetJsonOrNullAsync<CustomerResponse>($"api/orders/customers/{id}", ct);
}

/// <summary>Typed HttpClient сервісу Каталогу. BaseAddress = https+http://catalog-api.</summary>
public sealed class CatalogClient(HttpClient http)
{
    public Task<ProductResponse?> GetProductAsync(long id, CancellationToken ct) => http.GetJsonOrNullAsync<ProductResponse>($"api/catalog/products/{id}", ct);
}

/// <summary>Typed HttpClient сервісу Відгуків. BaseAddress = https+http://reviews-api.</summary>
public sealed class ReviewsClient(HttpClient http)
{
    public Task<RatingResponse?> GetRatingAsync(long productId, CancellationToken ct) =>
        http.GetJsonOrNullAsync<RatingResponse>($"api/reviews/products/{productId}/rating", ct);

    public Task<ReviewPageResponse?> GetLatestAsync(long productId, int count, CancellationToken ct) =>
        http.GetJsonOrNullAsync<ReviewPageResponse>($"api/reviews?productId={productId}&pageSize={count}&sortBy=newest", ct);
}

internal static class HttpClientJsonExtensions
{
    /// <summary>404 → null (сутності немає), інші помилки → виняток (обробляє агрегатор як часткову відмову).</summary>
    public static async Task<T?> GetJsonOrNullAsync<T>(this HttpClient http, string uri, CancellationToken ct)
    {
        using var response = await http.GetAsync(uri, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return default;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(ct);
    }
}
