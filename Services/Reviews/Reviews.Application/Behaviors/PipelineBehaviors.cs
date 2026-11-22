using System.Diagnostics;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Reviews.Application.Behaviors;

/// <summary>Логування кожного запиту/команди: тип, результат, тривалість — структуровано.</summary>
public sealed class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var name = typeof(TRequest).Name;
        logger.LogInformation("Handling {RequestName} {@Request}", name, request);
        try
        {
            var response = await next(ct);
            logger.LogInformation("Handled {RequestName} -> {ResponseType}", name, typeof(TResponse).Name);
            return response;
        }
        catch (Exception ex)
        {
            logger.LogWarning("Request {RequestName} failed: {ErrorType} {ErrorMessage}", name, ex.GetType().Name, ex.Message);
            throw;
        }
    }
}

/// <summary>Збирає ВСІ помилки валідаторів і кидає ValidationException до виклику обробника.</summary>
public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        if (!validators.Any()) return await next(ct);

        var context = new ValidationContext<TRequest>(request);
        var results = await Task.WhenAll(validators.Select(v => v.ValidateAsync(context, ct)));
        var failures = results.SelectMany(r => r.Errors).Where(f => f is not null).ToList();
        if (failures.Count > 0) throw new ValidationException(failures);

        return await next(ct);
    }
}

/// <summary>Попередження про повільні обробники (поріг 500 мс).</summary>
public sealed class PerformanceBehavior<TRequest, TResponse>(ILogger<PerformanceBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    private const int ThresholdMs = 500;

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var response = await next(ct);
        sw.Stop();
        if (sw.ElapsedMilliseconds > ThresholdMs)
            logger.LogWarning("Slow request {RequestName} took {ElapsedMs} ms (threshold {ThresholdMs} ms)",
                typeof(TRequest).Name, sw.ElapsedMilliseconds, ThresholdMs);
        return response;
    }
}
