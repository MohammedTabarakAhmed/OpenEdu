using Microsoft.AspNetCore.Mvc;
using OpenCampus.SharedKernel;

namespace OpenCampus.Api.ErrorHandling;

/// <summary>Translates application <see cref="Error"/> values to the status codes of SDD 15.2 in problem format (API-04).</summary>
public static class ProblemMapping
{
    public static int StatusCodeFor(ErrorType type) => type switch
    {
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        ErrorType.RuleViolation => StatusCodes.Status422UnprocessableEntity,
        _ => StatusCodes.Status400BadRequest,
    };

    public static ProblemDetails ToProblemDetails(this Error error, HttpContext httpContext)
    {
        var status = StatusCodeFor(error.Type);
        if (error.Type == ErrorType.Validation)
        {
            // API-04: validation failures carry a field-keyed error collection; the code names the field.
            return new ValidationProblemDetails(new Dictionary<string, string[]> { [error.Code] = [error.Message] })
            {
                Type = "urn:opencampus:error:validation",
                Title = "validation",
                Status = status,
                Detail = error.Message,
                Instance = httpContext.Request.Path,
            };
        }

        return new ProblemDetails
        {
            Type = $"urn:opencampus:error:{error.Code}",
            Title = error.Code,
            Status = status,
            Detail = error.Message,
            Instance = httpContext.Request.Path,
        };
    }

    public static ActionResult ToActionResult(this Result result, ControllerBase controller)
    {
        return result.IsSuccess
            ? controller.NoContent()
            : controller.StatusCode(StatusCodeFor(result.Error.Type), result.Error.ToProblemDetails(controller.HttpContext));
    }

    public static ActionResult<T> ToActionResult<T>(this Result<T> result, ControllerBase controller)
    {
        return result.IsSuccess
            ? controller.Ok(result.Value)
            : controller.StatusCode(StatusCodeFor(result.Error.Type), result.Error.ToProblemDetails(controller.HttpContext));
    }

    /// <summary>API-05: creation returns 201 with a Location header pointing at the retrieval action.</summary>
    public static ActionResult<T> ToCreatedResult<T>(this Result<T> result, ControllerBase controller, string actionName, Func<T, object?> routeValues)
    {
        return result.IsSuccess
            ? controller.CreatedAtAction(actionName, routeValues(result.Value), result.Value)
            : controller.StatusCode(StatusCodeFor(result.Error.Type), result.Error.ToProblemDetails(controller.HttpContext));
    }
}
