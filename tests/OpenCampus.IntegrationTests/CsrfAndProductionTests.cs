using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using OpenCampus.IntegrationTests.Identity;

namespace OpenCampus.IntegrationTests;

/// <summary>
/// Cross-site request forgery is defeated by design (the access credential is a bearer token held in memory, never a
/// cookie; the only cookie is SameSite=Strict and scoped to the authentication routes) — these tests prove it rather
/// than argue it. Also the Production start-up guard against demonstration data.
/// </summary>
[Collection(ApiCollection.Name)]
public class CsrfAndProductionTests(ApiFactory factory)
{
    [Fact]
    public async Task MutatingRequest_CarryingOnlyTheSessionCookie_IsRefused()
    {
        using var client = factory.CreateApiClient();
        var (_, accessToken, refreshToken) = await AuthTestSupport.LoginAsRoleAsync(factory, client, "Administrator");

        // A forged cross-site form post could at most carry the cookie: with no bearer header the fallback policy answers 401.
        var forged = new HttpRequestMessage(HttpMethod.Post, "/api/v1/programmes")
        {
            Content = JsonContent.Create(new { code = "CSRF1", nameEn = "Forged", nameAr = "مزوّر", durationMonths = 12 }),
        }.WithRefreshCookie(refreshToken);
        var response = await client.SendAsync(forged);
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // The same request with the bearer credential is accepted (or validated), proving the cookie was the only thing missing.
        var genuine = new HttpRequestMessage(HttpMethod.Post, "/api/v1/programmes")
        {
            Content = JsonContent.Create(new { code = $"CS{Guid.NewGuid():N}"[..10], nameEn = "Genuine", nameAr = "أصلي", durationMonths = 12 }),
        }.WithBearer(accessToken);
        var accepted = await client.SendAsync(genuine);
        ((int)accepted.StatusCode).ShouldBeOneOf(201, 400);
    }

    [Fact]
    public async Task SessionCookie_IsScopedToTheAuthenticationRoutes_AndStrict()
    {
        using var client = factory.CreateApiClient();
        var user = await AuthTestSupport.SeedUserAsync(factory, roles: "Learner");
        var response = await AuthTestSupport.LoginAsync(client, user.UserName, AuthTestSupport.DefaultPassword);
        var header = AuthTestSupport.ReadRefreshCookieHeader(response)!;

        header.ShouldContain("path=/api/v1/auth", Case.Insensitive);
        header.ShouldContain("samesite=strict", Case.Insensitive);
        header.ShouldContain("httponly", Case.Insensitive);
        header.ShouldContain("secure", Case.Insensitive);
    }

    [Fact]
    public void ProductionHost_RefusesToStart_WithDemonstrationDataEnabled()
    {
        using var production = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Provisioning:DemonstrationDataEnabled", "true");
        });

        var failure = Should.Throw<InvalidOperationException>(() => production.CreateClient());
        failure.Message.ShouldContain("DemonstrationDataEnabled");
    }
}
