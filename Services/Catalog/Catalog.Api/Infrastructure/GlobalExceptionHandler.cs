using Catalog.Domain.Exceptions;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Catalog.Api.Infrastructure;

/// <summary>
/// Глобальний обробник: NotFound → 404, Validation → 400 (ValidationProblemDetails), Conflict/Domain → 409, інше → 500.
/// Деталі винятку показуються лише в Development; виняток логується один раз — тут.
/// </summary>
public sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetails,
    IHostEnvironment environment,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        ProblemDetails problem = exception switch
        {
            ValidationException validation => new ValidationProblemDetails(
                validation.Errors.GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()))
            {
                Status = StatusCodes.Status400BadRequest, Title = "Validation failed"
            },
            NotFoundException => new ProblemDetails { Status = StatusCodes.Status404NotFound, Title = "Resource not found" },
            ConflictException => new ProblemDetails { Status = StatusCodes.Status409Conflict, Title = "Conflict" },
            DomainException => new ProblemDetails { Status = StatusCodes.Status409Conflict, Title = "Domain rule violated" },
            _ => new ProblemDetails { Status = StatusCodes.Status500InternalServerError, Title = "Internal server error" }
        };

        var status = problem.Status!.Value;
        if (status >= 500)
            logger.LogError(exception, "Unhandled exception on {Method} {Path}", context.Request.Method, context.Request.Path);
        else
            logger.LogWarning("Request {Method} {Path} -> {StatusCode}: {Reason}", context.Request.Method, context.Request.Path, status, exception.Message);

        if (status < 500 || environment.IsDevelopment()) problem.Detail = exception.Message;
        if (environment.IsDevelopment() && status >= 500) problem.Extensions["exception"] = exception.GetType().Name;
        problem.Instance = context.Request.Path;
        problem.Extensions["traceId"] = context.TraceIdentifier;

        context.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = problem, Exception = exception });
    }
}
