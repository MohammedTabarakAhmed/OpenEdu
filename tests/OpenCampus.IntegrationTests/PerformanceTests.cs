using System.Diagnostics;
using System.Net.Http.Json;
using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using OpenCampus.IntegrationTests.Identity;
using OpenCampus.Sis.Application.Abstractions;
using OpenCampus.Sis.Domain.Programmes;

namespace OpenCampus.IntegrationTests;

/// <summary>Section 18.4 caching, compressed payloads, and timing evidence for NFR-01 / NFR-02 on the real host.</summary>
[Collection(ApiCollection.Name)]
public class PerformanceTests(ApiFactory factory)
{
    private static HttpRequestMessage Get(string url, string token, string? encoding = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (encoding is not null)
        {
            request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue(encoding));
        }
        return request;
    }

    [Fact]
    public async Task JsonResponses_AreCompressedWhenNegotiated_AndUncompressedOtherwise()
    {
        using var client = factory.CreateApiClient();
        var (_, token, _) = await AuthTestSupport.LoginAsRoleAsync(factory, client, "Administrator");

        var compressed = await client.SendAsync(Get("/api/v1/programmes?page=1&pageSize=20", token, "br"));
        compressed.StatusCode.ShouldBe(HttpStatusCode.OK);
        compressed.Content.Headers.ContentEncoding.ShouldContain("br");

        var plain = await client.SendAsync(Get("/api/v1/programmes?page=1&pageSize=20", token));
        plain.Content.Headers.ContentEncoding.ShouldBeEmpty();

        // The anonymous health check (text/plain) compresses too; the 401 challenge carries no body to compress.
        var health = await client.SendAsync(Get("/api/health", token, "gzip"));
        health.Content.Headers.ContentEncoding.ShouldContain("gzip");
    }

    // 18.4: the batched programme lookup is served from the in-process cache until amended; the single read is not cached.
    [Fact]
    public async Task ProgrammeLookup_IsCachedUntilInvalidated()
    {
        using var scope = factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IProgrammeRepository>();
        var cache = scope.ServiceProvider.GetRequiredService<IReferenceDataCache>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<ISisUnitOfWork>();

        var programme = Programme.Create($"CACHE-{Guid.NewGuid():N}"[..16], "Cache test", "اختبار", 12);
        repository.Add(programme);
        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        var first = await repository.FindManyAsync([programme.Id], CancellationToken.None);
        first[programme.Id].NameEn.ShouldBe("Cache test");

        // Change the row behind the cache's back through a fresh context: the batched lookup still answers from cache.
        using (var other = factory.Services.CreateScope())
        {
            var tracked = await other.ServiceProvider.GetRequiredService<IProgrammeRepository>().FindByIdAsync(programme.Id, CancellationToken.None);
            tracked!.Amend("Cache test amended", tracked.NameAr, tracked.DurationMonths);
            await other.ServiceProvider.GetRequiredService<ISisUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        var cached = await repository.FindManyAsync([programme.Id], CancellationToken.None);
        cached[programme.Id].NameEn.ShouldBe("Cache test");
        using (var reader = factory.Services.CreateScope())
        {
            // The single-entity read is never cached: a fresh context sees the amendment immediately.
            (await reader.ServiceProvider.GetRequiredService<IProgrammeRepository>().FindByIdAsync(programme.Id, CancellationToken.None))!
                .NameEn.ShouldBe("Cache test amended");
        }

        cache.InvalidateProgramme(programme.Id);
        using var fresh = factory.Services.CreateScope();
        var refreshed = await fresh.ServiceProvider.GetRequiredService<IProgrammeRepository>().FindManyAsync([programme.Id], CancellationToken.None);
        refreshed[programme.Id].NameEn.ShouldBe("Cache test amended");
    }

    // NFR-01 / NFR-02 evidence: 95th-percentile server processing for a paged list and a single-entity read, in-process.
    [Fact]
    public async Task PagedListAndSingleRead_MeetThe95thPercentileTargets()
    {
        using var client = factory.CreateApiClient();
        var (_, token, _) = await AuthTestSupport.LoginAsRoleAsync(factory, client, "Administrator");

        var list = await client.SendAsync(Get("/api/v1/programmes?page=1&pageSize=20", token));
        list.StatusCode.ShouldBe(HttpStatusCode.OK);
        var page = await list.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var firstId = page.GetProperty("items").EnumerateArray().First().GetProperty("id").GetString();

        static async Task<double> P95(Func<Task> action, int samples = 30)
        {
            for (var i = 0; i < 3; i++) await action(); // warm-up: JIT, connection pool, query plan
            var times = new List<double>(samples);
            for (var i = 0; i < samples; i++)
            {
                var watch = Stopwatch.StartNew();
                await action();
                times.Add(watch.Elapsed.TotalMilliseconds);
            }
            times.Sort();
            return times[(int)Math.Ceiling(0.95 * samples) - 1];
        }

        var listP95 = await P95(async () => (await client.SendAsync(Get("/api/v1/programmes?page=1&pageSize=20", token))).EnsureSuccessStatusCode());
        var readP95 = await P95(async () => (await client.SendAsync(Get($"/api/v1/programmes/{firstId}", token))).EnsureSuccessStatusCode());

        listP95.ShouldBeLessThan(500, $"NFR-01 paged list p95 was {listP95:F0} ms");
        readP95.ShouldBeLessThan(200, $"NFR-02 single read p95 was {readP95:F0} ms");
    }
}
