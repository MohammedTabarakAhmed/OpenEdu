using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using OpenCampus.SharedKernel;

namespace OpenCampus.Api.ErrorHandling;

/// <summary>
/// SDD 18.3: the single mechanism translating exceptions to responses. Unhandled
/// exceptions yield a generic 500; full detail goes to the log only (SEC-21).
/// </summary>
public sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetails,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title, detail) = exception switch
        {
            EntityNotFoundException => (StatusCodes.Status404NotFound, "not_found", "The requested resource was not found."),
            // Section 14 rule: the title carries the rule reference so a client can distinguish BR-01 from BR-02 (API-07).
            BusinessRuleViolationException rule => (StatusCodes.Status422UnprocessableEntity, rule.RuleCode, rule.Message),
            DomainException domain => (StatusCodes.Status422UnprocessableEntity, "rule_violation", domain.Message),
            OperationCanceledException => (StatusCodes.Status499ClientClosedRequest, "request_cancelled", "The request was cancelled."),
            _ => (StatusCodes.Status500InternalServerError, "internal_error", "An unexpected error occurred."),
        };

        if (status == StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled exception for {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
        }
        else
        {
            logger.LogInformation(exception, "Request {Method} {Path} rejected with {Status}", httpContext.Request.Method, httpContext.Request.Path, status);
        }

        httpContext.Response.StatusCode = status;

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails
            {
                Type = $"urn:opencampus:error:{title}",
                Title = title,
                Status = status,
                Detail = detail,
                Instance = httpContext.Request.Path,
            },
        });
    }
}
