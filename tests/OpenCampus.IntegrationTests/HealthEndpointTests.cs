using System.Net;
using OpenCampus.Api.Middleware;

namespace OpenCampus.IntegrationTests;

[Collection(ApiCollection.Name)]
public class HealthEndpointTests(ApiFactory factory)
{
    [Fact]
    public async Task Health_ReturnsHealthy()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/health");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldBe("Healthy");
    }

    [Fact]
    public async Task Response_CarriesCorrelationIdHeader()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/health");

        response.Headers.TryGetValues(CorrelationIdMiddleware.HeaderName, out var values).ShouldBeTrue();
        Guid.TryParse(values!.Single(), out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Response_EchoesSuppliedCorrelationId()
    {
        using var client = factory.CreateClient();
        var supplied = Guid.CreateVersion7().ToString();
        client.DefaultRequestHeaders.Add(CorrelationIdMiddleware.HeaderName, supplied);

        var response = await client.GetAsync("/api/health");

        response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single().ShouldBe(supplied);
    }
}
