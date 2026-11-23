using System.Net.Http.Headers;
using Duende.IdentityModel.Client;

namespace Platform.Aggregator.Auth;

public sealed class AggregatorClientOptions
{
    public const string SectionName = "IdentityServer:Aggregator";
    public string ClientId { get; set; } = "aggregator";
    public string ClientSecret { get; set; } = string.Empty;
    public string Scope { get; set; } = "orders.read catalog.read reviews.read";
}

/// <summary>
/// Отримує access-токен IdentityServer за Client Credentials (client "aggregator") і кешує його до закінчення строку.
/// Named HttpClient "identityserver" має адресу https+http://identityserver (service discovery).
/// </summary>
public sealed class ClientCredentialsTokenProvider(
    IHttpClientFactory httpClientFactory,
    Microsoft.Extensions.Options.IOptions<AggregatorClientOptions> options,
    ILogger<ClientCredentialsTokenProvider> logger)
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _token;
    private DateTime _expiresAt;

    public async Task<string?> GetTokenAsync(CancellationToken ct)
    {
        if (_token is not null && DateTime.UtcNow < _expiresAt) return _token;
        await _lock.WaitAsync(ct);
        try
        {
            if (_token is not null && DateTime.UtcNow < _expiresAt) return _token;

            var client = httpClientFactory.CreateClient("identityserver");
            var response = await client.RequestClientCredentialsTokenAsync(new ClientCredentialsTokenRequest
            {
                Address = new Uri(client.BaseAddress!, "connect/token").ToString(),
                ClientId = options.Value.ClientId,
                ClientSecret = options.Value.ClientSecret,
                Scope = options.Value.Scope
            }, ct);

            if (response.IsError)
            {
                logger.LogWarning("Client credentials token request failed: {Error}", response.Error);
                return null;
            }

            _token = response.AccessToken;
            _expiresAt = DateTime.UtcNow.AddSeconds(Math.Max(30, response.ExpiresIn - 60)); // оновлюємо за хвилину до закінчення
            logger.LogInformation("Obtained client credentials token for scopes {Scopes}, expires in {ExpiresIn}s", options.Value.Scope, response.ExpiresIn);
            return _token;
        }
        finally
        {
            _lock.Release();
        }
    }
}

/// <summary>Додає Bearer-токен Aggregator до вихідних HTTP- і gRPC-викликів мікросервісів.</summary>
public sealed class ClientCredentialsTokenHandler(ClientCredentialsTokenProvider tokens) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (await tokens.GetTokenAsync(ct) is { } token)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await base.SendAsync(request, ct);
    }
}
