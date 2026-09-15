using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace OpenCampus.Api.Validation;

/// <summary>
/// SDD 18.2: validators declared in the application layer are invoked here, centrally,
/// for every action argument that has one. Failures become a 400 with field-keyed detail (API-04).
/// </summary>
public sealed class ValidationFilter(IServiceProvider services) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        foreach (var argument in context.ActionArguments.Values)
        {
            if (argument is null)
            {
                continue;
            }

            if (services.GetService(typeof(IValidator<>).MakeGenericType(argument.GetType())) is not IValidator validator)
            {
                continue;
            }

            var result = await validator.ValidateAsync(new ValidationContext<object>(argument), context.HttpContext.RequestAborted);
            if (result.IsValid)
            {
                continue;
            }

            var errors = result.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());

            context.Result = new BadRequestObjectResult(new ValidationProblemDetails(errors)
            {
                Type = "urn:opencampus:error:validation",
                Title = "validation",
                Status = StatusCodes.Status400BadRequest,
                Detail = "One or more fields failed validation.",
                Instance = context.HttpContext.Request.Path,
            });
            return;
        }

        await next();
    }
}
