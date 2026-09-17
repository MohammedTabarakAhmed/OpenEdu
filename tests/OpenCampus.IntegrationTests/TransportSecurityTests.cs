using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using OpenCampus.Api.Security;
using OpenCampus.Identity.Application.Authentication;

namespace OpenCampus.IntegrationTests;

/// <summary>Section 16.6: transport security headers (SEC-27, SEC-28) and origin restriction (SEC-29).</summary>
[Collection(ApiCollection.Name)]
public class TransportSecurityTests(ApiFactory factory)
{
    private const string KnownOrigin = "https://client.opencampus.local";

    private static void ShouldCarrySecurityHeaders(HttpResponseMessage response)
    {
        response.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
        response.Headers.GetValues("X-Frame-Options").ShouldBe(["DENY"]);
        response.Headers.GetValues("Referrer-Policy").ShouldBe(["no-referrer"]);
        response.Headers.GetValues("Content-Security-Policy").ShouldBe([SecurityHeadersMiddleware.ContentSecurityPolicy]);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
    }

    // SEC-27: strict transport security on any named host; "localhost" is excluded by the framework because a pinned
    // localhost would apply to every other local project (and port) on the workstation for a year.
    [Fact]
    public async Task HttpsResponse_OnANamedHost_CarriesStrictTransportSecurity_AndLocalhostIsExempt()
    {
        using var named = factory.CreateClient(new() { BaseAddress = new Uri("https://api.opencampus.test"), HandleCookies = false });
        var response = await named.GetAsync("/api/health");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var hsts = response.Headers.GetValues("Strict-Transport-Security").Single();
        hsts.ShouldContain("max-age=31536000");
        hsts.ShouldContain("includeSubDomains");

        using var local = factory.CreateApiClient();
        (await local.GetAsync("/api/health")).Headers.Contains("Strict-Transport-Security").ShouldBeFalse();
    }

    [Fact]
    public async Task EveryResponseShape_CarriesTheSecurityHeaders()
    {
        using var client = factory.CreateApiClient();

        // A successful anonymous response.
        var health = await client.GetAsync("/api/health");
        health.StatusCode.ShouldBe(HttpStatusCode.OK);
        ShouldCarrySecurityHeaders(health);

        // An authentication challenge (SEC-10).
        var challenge = await client.GetAsync("/api/v1/me/certificates");
        challenge.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        ShouldCarrySecurityHeaders(challenge);

        // A problem response produced by the validation pipeline (API-04).
        var problem = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("", ""));
        ((int)problem.StatusCode).ShouldBeOneOf(400, 401);
        ShouldCarrySecurityHeaders(problem);

        // An unknown route.
        var missing = await client.GetAsync("/api/v1/no-such-route");
        missing.StatusCode.ShouldBeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Unauthorized);
        ShouldCarrySecurityHeaders(missing);
    }

    [Fact]
    public async Task CrossOriginRequest_WithNoConfiguredOrigins_IsNotGranted()
    {
        using var client = factory.CreateApiClient();

        using var preflight = new HttpRequestMessage(HttpMethod.Options, "/api/v1/auth/login");
        preflight.Headers.Add("Origin", KnownOrigin);
        preflight.Headers.Add("Access-Control-Request-Method", "POST");
        var response = await client.SendAsync(preflight);

        response.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
        response.Headers.Contains("Access-Control-Allow-Credentials").ShouldBeFalse();

        using var simple = new HttpRequestMessage(HttpMethod.Get, "/api/health");
        simple.Headers.Add("Origin", KnownOrigin);
        var simpleResponse = await client.SendAsync(simple);

        simpleResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        simpleResponse.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
    }

    [Fact]
    public async Task CrossOriginRequest_FromAConfiguredOrigin_IsGrantedExactlyAndOthersAreNot()
    {
        using var host = factory.WithWebHostBuilder(builder => builder.UseSetting("Cors:AllowedOrigins:0", KnownOrigin));
        using var client = host.CreateClient(new() { BaseAddress = new Uri("https://localhost"), HandleCookies = false });

        using var preflight = new HttpRequestMessage(HttpMethod.Options, "/api/v1/auth/login");
        preflight.Headers.Add("Origin", KnownOrigin);
        preflight.Headers.Add("Access-Control-Request-Method", "POST");
        preflight.Headers.Add("Access-Control-Request-Headers", "content-type");
        var granted = await client.SendAsync(preflight);

        granted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        granted.Headers.GetValues("Access-Control-Allow-Origin").ShouldBe([KnownOrigin]);
        granted.Headers.GetValues("Access-Control-Allow-Credentials").ShouldBe(["true"]);
        ShouldCarrySecurityHeaders(granted);

        using var other = new HttpRequestMessage(HttpMethod.Options, "/api/v1/auth/login");
        other.Headers.Add("Origin", "https://evil.example");
        other.Headers.Add("Access-Control-Request-Method", "POST");
        var refused = await client.SendAsync(other);

        refused.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
    }

    [Theory]
    [InlineData("*")]
    [InlineData("https://*.opencampus.local")]
    [InlineData("http://client.opencampus.local")]
    [InlineData("https://client.opencampus.local/app")]
    public void Startup_RefusesPermissiveOrInsecureOrigins(string origin)
    {
        var options = new CorsOptions { AllowedOrigins = [origin] };

        Should.Throw<InvalidOperationException>(options.Validate).Message.ShouldContain("Cors:AllowedOrigins");
    }

    [Fact]
    public void Startup_AcceptsExplicitHttpsOrigins()
    {
        var options = new CorsOptions { AllowedOrigins = [KnownOrigin, "https://localhost:4200"] };

        Should.NotThrow(options.Validate);
    }
}
