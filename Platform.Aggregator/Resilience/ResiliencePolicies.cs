using Grpc.Core;
using Grpc.Net.Client.Configuration;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace Platform.Aggregator.Resilience;

/// <summary>
/// Політики стійкості клієнтів Aggregator, підібрані за критичністю сервісу (обґрунтування — README, розділ Лаб. 07).
/// Стандартний обробник з ServiceDefaults спершу прибирається (RemoveAllResilienceHandlers), щоб не нашаровувати конвеєри.
/// </summary>
public static class ResiliencePolicies
{
    /// <summary>Прибирає стандартний обробник з ServiceDefaults і додає власний — без нашарування.</summary>
    private static IHttpClientBuilder WithCustomPipeline(this IHttpClientBuilder builder, Action<HttpStandardResilienceOptions, IServiceProvider> configure)
    {
#pragma warning disable EXTEXP0001 // RemoveAllResilienceHandlers — експериментальний API, рекомендований документацією Aspire
        builder.RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001
        builder.AddStandardResilienceHandler().Configure(configure);
        return builder;
    }

    /// <summary>
    /// Каталог — КРИТИЧНИЙ: без товарів картку не зібрати. Більше повторів, чутливіший circuit breaker
    /// (розмикається при 30% збоїв), повтори лише для безпечних (GET) методів.
    /// </summary>
    public static IHttpClientBuilder AddCriticalResilience(this IHttpClientBuilder builder, string pipelineName) =>
        builder.WithCustomPipeline((o, sp) =>
        {
            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger($"Resilience.{pipelineName}");

            o.Retry.MaxRetryAttempts = 5;
            o.Retry.Delay = TimeSpan.FromMilliseconds(500);
            o.Retry.BackoffType = DelayBackoffType.Exponential;
            o.Retry.UseJitter = true;                   // розносимо повтори в часі — без thundering herd
            o.Retry.DisableForUnsafeHttpMethods();      // POST/PUT/PATCH/DELETE не повторюються
            o.Retry.OnRetry = args =>
            {
                logger.LogWarning("Retry {Attempt} for {Pipeline} after {Delay} ms: {Reason}",
                    args.AttemptNumber + 1, pipelineName, args.RetryDelay.TotalMilliseconds,
                    args.Outcome.Exception?.Message ?? args.Outcome.Result?.StatusCode.ToString());
                return ValueTask.CompletedTask;
            };

            o.CircuitBreaker.FailureRatio = 0.3;          // чутливіший за стандартні 0.1@100 req при малому трафіку стенду
            o.CircuitBreaker.MinimumThroughput = 10;      // навчальний стенд: мало запитів
            o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30); // ≥ 2 × AttemptTimeout
            o.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(15);
            o.CircuitBreaker.OnOpened = args =>
            {
                logger.LogError("Circuit OPENED for {Pipeline} for {BreakDuration}", pipelineName, args.BreakDuration);
                return ValueTask.CompletedTask;
            };
            o.CircuitBreaker.OnClosed = _ =>
            {
                logger.LogInformation("Circuit CLOSED for {Pipeline}", pipelineName);
                return ValueTask.CompletedTask;
            };
            o.CircuitBreaker.OnHalfOpened = _ =>
            {
                logger.LogWarning("Circuit HALF-OPEN for {Pipeline}, probing", pipelineName);
                return ValueTask.CompletedTask;
            };

            // 5 повторів × 3 с + сумарні затримки (~0.5+1+2+4+8 с) вкладаються у 30 с
            o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(3);
            o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(30);
        });

    /// <summary>
    /// Відгуки — НЕКРИТИЧНІ: Aggregator і без них поверне часткову відповідь. Один повтор, короткі таймаути,
    /// менш чутливий breaker (70%) — швидко здатися й показати warning.
    /// </summary>
    public static IHttpClientBuilder AddNonCriticalResilience(this IHttpClientBuilder builder, string pipelineName) =>
        builder.WithCustomPipeline((o, sp) =>
        {
            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger($"Resilience.{pipelineName}");

            o.Retry.MaxRetryAttempts = 1;
            o.Retry.Delay = TimeSpan.FromMilliseconds(200);
            o.Retry.BackoffType = DelayBackoffType.Exponential;
            o.Retry.UseJitter = true;
            o.Retry.DisableForUnsafeHttpMethods();

            o.CircuitBreaker.FailureRatio = 0.7;
            o.CircuitBreaker.MinimumThroughput = 10;
            o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(10);  // ≥ 2 × 2 с
            o.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(10);
            o.CircuitBreaker.OnOpened = args =>
            {
                logger.LogError("Circuit OPENED for {Pipeline} for {BreakDuration}", pipelineName, args.BreakDuration);
                return ValueTask.CompletedTask;
            };

            o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(2);
            o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(5);
        });

    /// <summary>
    /// gRPC повертає Unavailable/DeadlineExceeded у трейлерах з HTTP 200, тому HTTP-обробник їх не повторює.
    /// Повтори за gRPC-статусами — вбудованою retry-політикою каналу (ServiceConfig).
    /// </summary>
    public static IHttpClientBuilder WithGrpcRetry(this IHttpClientBuilder builder, int maxAttempts, TimeSpan initialBackoff) =>
        builder.ConfigureChannel(channel => channel.ServiceConfig = new ServiceConfig
        {
            MethodConfigs =
            {
                new MethodConfig
                {
                    Names = { MethodName.Default },
                    RetryPolicy = new RetryPolicy
                    {
                        MaxAttempts = maxAttempts,
                        InitialBackoff = initialBackoff,
                        MaxBackoff = TimeSpan.FromSeconds(4),
                        BackoffMultiplier = 2,
                        RetryableStatusCodes = { StatusCode.Unavailable, StatusCode.ResourceExhausted }
                    }
                }
            }
        });
}
