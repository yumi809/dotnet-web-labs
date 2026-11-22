using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using Reviews.Application.Common;
using Reviews.Domain.Common;

namespace Reviews.WebApi.Infrastructure;

/// <summary>
/// ProblemDetails з підтримкою MongoDB-винятків: NotFound → 404, Validation → 400, Conflict/Domain → 409,
/// DuplicateKey → 409, MongoConnectionException/Timeout → 503, інше → 500.
/// </summary>
public sealed class GlobalExceptionHandler(IProblemDetailsService problemDetails, IHostEnvironment env, ILogger<GlobalExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        ProblemDetails problem = exception switch
        {
            ValidationException v => new ValidationProblemDetails(v.Errors.GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()))
            { Status = 400, Title = "Validation failed" },
            NotFoundException => new ProblemDetails { Status = 404, Title = "Resource not found" },
            ConflictException => new ProblemDetails { Status = 409, Title = "Conflict" },
            DomainException => new ProblemDetails { Status = 409, Title = "Domain rule violated" },
            MongoWriteException { WriteError.Category: ServerErrorCategory.DuplicateKey } =>
                new ProblemDetails { Status = 409, Title = "Duplicate key" },
            MongoConnectionException or TimeoutException =>
                new ProblemDetails { Status = 503, Title = "Database unavailable" },
            _ => new ProblemDetails { Status = 500, Title = "Internal server error" }
        };

        var status = problem.Status!.Value;
        if (status >= 500)
            logger.LogError(exception, "Request {Method} {Path} failed with {StatusCode}", context.Request.Method, context.Request.Path, status);
        else
            logger.LogWarning("Request {Method} {Path} -> {StatusCode}: {Reason}", context.Request.Method, context.Request.Path, status, exception.Message);

        if (status < 500 || env.IsDevelopment()) problem.Detail = exception.Message;
        if (status == 503) context.Response.Headers.RetryAfter = "10";
        problem.Instance = context.Request.Path;
        problem.Extensions["traceId"] = context.TraceIdentifier;

        context.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = problem, Exception = exception });
    }
}
