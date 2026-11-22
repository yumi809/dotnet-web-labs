using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Orders.Domain.Exceptions;

namespace Orders.Api.Infrastructure;

/// <summary>
/// Єдиний глобальний обробник помилок у форматі ProblemDetails (RFC 7807):
/// NotFound → 404, BusinessConflict → 409, Validation → 400, інше → 500.
/// </summary>
public sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    IHostEnvironment environment,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken ct)
    {
        var (status, title) = exception switch
        {
            NotFoundException => (StatusCodes.Status404NotFound, "Resource not found"),
            BusinessConflictException => (StatusCodes.Status409Conflict, "Business rule conflict"),
            DomainValidationException => (StatusCodes.Status400BadRequest, "Validation failed"),
            OperationCanceledException => (499, "Request cancelled"),
            _ => (StatusCodes.Status500InternalServerError, "Internal server error")
        };

        if (status >= 500)
            logger.LogError(exception, "Unhandled exception for {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
        else
            logger.LogWarning("Request {Method} {Path} failed with {StatusCode}: {Message}",
                httpContext.Request.Method, httpContext.Request.Path, status, exception.Message);

        ProblemDetails problem = exception is DomainValidationException validation
            ? new ValidationProblemDetails(validation.Errors.ToDictionary(k => k.Key, v => v.Value))
            : new ProblemDetails();

        problem.Status = status;
        problem.Title = title;
        problem.Detail = status < 500 || environment.IsDevelopment() ? exception.Message : null;
        problem.Instance = httpContext.Request.Path;
        problem.Extensions["traceId"] = httpContext.TraceIdentifier;

        httpContext.Response.StatusCode = status;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception
        });
    }
}
