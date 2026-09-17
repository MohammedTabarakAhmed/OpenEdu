using Microsoft.Extensions.Options;
using OpenCampus.Api.Middleware;

namespace OpenCampus.Api.Security;

/// <summary>
/// SEC-29: the origins other than the host's own that may call the API from a browser. Empty (the default and the
/// section 9.2 topology, where client and API share an origin or a development proxy makes them appear to) means
/// no cross-origin request is permitted at all. A wildcard is refused at start-up because the client carries a
/// credential (the refresh cookie) and a permissive origin combined with credentials is exactly what SEC-29 forbids.
/// </summary>
public sealed class CorsOptions
{
    public const string SectionName = "Cors";

    public string[] AllowedOrigins { get; set; } = [];

    public void Validate()
    {
        foreach (var origin in AllowedOrigins)
        {
            if (origin == "*" || origin.Contains('*'))
            {
                throw new InvalidOperationException("Cors:AllowedOrigins must list explicit origins; a wildcard is not permitted (SEC-29).");
            }

            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || uri.PathAndQuery != "/")
            {
                throw new InvalidOperationException($"Cors:AllowedOrigins entry '{origin}' must be an absolute https origin without a path (SEC-27, SEC-29).");
            }
        }
    }
}

/// <summary>
/// SEC-27 / SEC-28: transport security headers on every response, including problem responses and challenges.
/// The API serves data (JSON, PDF, problem documents) and never a page, so the Content Security Policy forbids every
/// source and refuses framing; the client page carries its own policy from index.html.
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public const string ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'; sandbox";

    public Task InvokeAsync(HttpContext context)
    {
        // Applied when the response starts, so a response cleared and rewritten by the exception handler keeps them.
        context.Response.OnStarting(static state =>
        {
            var headers = ((HttpContext)state).Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers["Content-Security-Policy"] = ContentSecurityPolicy;
            // API responses are principal-specific; a shared cache must never serve one caller's data to another.
            headers.CacheControl = "no-store";
            return Task.CompletedTask;
        }, context);
        return next(context);
    }
}

public static class TransportSecurity
{
    public const string CorsPolicy = "known-origins";

    public static IServiceCollection AddOpenCampusTransportSecurity(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<CorsOptions>()
            .Bind(configuration.GetSection(CorsOptions.SectionName))
            .Validate(options => { options.Validate(); return true; })
            .ValidateOnStart();

        // SEC-27: browsers pin the site to HTTPS for a year once they have seen it over TLS.
        services.AddHsts(hsts =>
        {
            hsts.MaxAge = TimeSpan.FromDays(365);
            hsts.IncludeSubDomains = true;
        });

        services.AddCors();
        services.AddOptions<Microsoft.AspNetCore.Cors.Infrastructure.CorsOptions>()
            .Configure<IOptions<CorsOptions>>((cors, known) => cors.AddPolicy(CorsPolicy, policy =>
            {
                if (known.Value.AllowedOrigins.Length == 0)
                {
                    // No origin listed: the policy matches nothing, so no Access-Control-Allow-Origin is ever emitted.
                    return;
                }

                policy.WithOrigins(known.Value.AllowedOrigins)
                    .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE")
                    .WithHeaders("Authorization", "Content-Type", CorrelationIdMiddleware.HeaderName)
                    .WithExposedHeaders(CorrelationIdMiddleware.HeaderName, "Content-Disposition", "X-Content-SHA256")
                    .AllowCredentials();
            }));

        return services;
    }
}
