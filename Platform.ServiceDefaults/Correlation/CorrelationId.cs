using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Context;

namespace Platform.ServiceDefaults.Correlation;

public interface ICorrelationIdAccessor
{
    string? CorrelationId { get; }
}

/// <summary>Зберігає CorrelationId поточного потоку виконання (AsyncLocal) — доступний і поза HTTP-запитом.</summary>
public sealed class CorrelationIdAccessor : ICorrelationIdAccessor
{
    private static readonly AsyncLocal<string?> Current = new();
    public string? CorrelationId => Current.Value;
    internal static void Set(string? value) => Current.Value = value;
}

/// <summary>
/// Бере X-Correlation-Id з вхідного запиту або генерує новий, кладе його в LogContext,
/// у заголовок відповіді та в тег поточного Activity.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers.TryGetValue(HeaderName, out var values) && !string.IsNullOrWhiteSpace(values)
            ? values.ToString()
            : Guid.NewGuid().ToString("N");

        CorrelationIdAccessor.Set(correlationId);
        // Записуємо назад у заголовки запиту: YARP пересилає вхідні заголовки, тож сервіси отримають той самий Id
        context.Request.Headers[HeaderName] = correlationId;
        context.TraceIdentifier = correlationId;
        Activity.Current?.SetTag("correlation.id", correlationId);
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }
}

/// <summary>Передає той самий X-Correlation-Id у всі вихідні HTTP-запити (HttpClientFactory).</summary>
public sealed class CorrelationIdDelegatingHandler(ICorrelationIdAccessor accessor) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (accessor.CorrelationId is { } id && !request.Headers.Contains(CorrelationIdMiddleware.HeaderName))
            request.Headers.Add(CorrelationIdMiddleware.HeaderName, id);
        return base.SendAsync(request, cancellationToken);
    }
}

public static class CorrelationIdExtensions
{
    public static IServiceCollection AddCorrelationId(this IServiceCollection services)
    {
        services.AddSingleton<ICorrelationIdAccessor, CorrelationIdAccessor>();
        services.AddTransient<CorrelationIdDelegatingHandler>();
        return services;
    }

    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app) =>
        app.UseMiddleware<CorrelationIdMiddleware>();
}
