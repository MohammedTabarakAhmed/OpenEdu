using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using OpenCampus.Identity.Application.Authentication;

namespace OpenCampus.IntegrationTests.Identity;

/// <summary>SEC-16: rate limiting on authentication endpoints, verified on a host configured with a small window.</summary>
[Collection(ApiCollection.Name)]
public class RateLimitingTests(ApiFactory factory)
{
    private const int Limit = 3;

    [Fact]
    public async Task Login_BeyondPermittedRequests_Returns429ProblemWithRetryAfter()
    {
        using var limited = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("AccountProtection:RateLimitPermittedRequests", Limit.ToString());
            builder.UseSetting("AccountProtection:RateLimitWindow", "00:01:00");
        });
        using var client = limited.CreateClient();
        var request = new LoginRequest("nobody", "irrelevant-password");

        for (var i = 0; i < Limit; i++)
        {
            (await client.PostAsJsonAsync("/api/v1/auth/login", request)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        var rejected = await client.PostAsJsonAsync("/api/v1/auth/login", request);

        rejected.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        rejected.Headers.RetryAfter.ShouldNotBeNull();
        var problem = await rejected.Content.ReadFromJsonAsync<ProblemDetails>(AuthTestSupport.Json);
        problem!.Status.ShouldBe(429);
        problem.Title.ShouldBe("rate_limited");
    }

    [Fact]
    public async Task Refresh_IsAlsoRateLimited()
    {
        using var limited = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("AccountProtection:RateLimitPermittedRequests", Limit.ToString()));
        using var client = limited.CreateClient();

        for (var i = 0; i < Limit; i++)
        {
            (await client.PostAsync("/api/v1/auth/refresh", null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        (await client.PostAsync("/api/v1/auth/refresh", null)).StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }
}
