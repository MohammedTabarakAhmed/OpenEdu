using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using OpenCampus.Identity.Application.Security;

namespace OpenCampus.Api.Security;

/// <summary>SEC-16: rate limiting on authentication endpoints, keyed by originating address, from Appendix B configuration.</summary>
public static class RateLimitPolicies
{
    public const string Authentication = "authentication";

    public static IServiceCollection AddOpenCampusRateLimiting(this IServiceCollection services, AccountProtectionOptions options)
    {
        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            limiter.AddPolicy(Authentication, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = options.RateLimitPermittedRequests,
                        Window = options.RateLimitWindow,
                        QueueLimit = 0,
                        AutoReplenishment = true,
                    }));

            limiter.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();
                }

                var problemDetails = context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
                await problemDetails.WriteAsync(new ProblemDetailsContext
                {
                    HttpContext = context.HttpContext,
                    ProblemDetails = new ProblemDetails
                    {
                        Type = "urn:opencampus:error:rate_limited",
                        Title = "rate_limited",
                        Status = StatusCodes.Status429TooManyRequests,
                        Detail = "Too many requests. Try again later.",
                        Instance = context.HttpContext.Request.Path,
                    },
                });
            };
        });

        return services;
    }
}
