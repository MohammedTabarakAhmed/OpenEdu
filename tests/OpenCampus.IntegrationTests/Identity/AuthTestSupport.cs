using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenCampus.Api.Controllers;
using OpenCampus.Api.Security;
using OpenCampus.Identity.Application.Authentication;
using OpenCampus.Identity.Application.Security;
using OpenCampus.Identity.Domain.Audit;
using OpenCampus.Identity.Domain.Users;
using OpenCampus.Identity.Infrastructure.Persistence;
using OpenCampus.Identity.Infrastructure.Security;

namespace OpenCampus.IntegrationTests.Identity;

/// <summary>Helpers shared by the authentication tests: seeding users, calling endpoints, reading cookies and audit rows.</summary>
internal static class AuthTestSupport
{
    public const string DefaultPassword = "Correct-Horse-Battery-Staple-1";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static async Task<User> SeedUserAsync(ApiFactory factory, string? password = null, bool active = true, params string[] roles)
    {
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var hasher = factory.Services.GetRequiredService<IPasswordHasher>();
        var user = User.Create($"{suffix}@example.org", $"user_{suffix}", hasher.Hash(password ?? DefaultPassword), $"Test {suffix}", "اختبار");
        if (!active)
        {
            user.Deactivate();
        }

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        foreach (var roleName in roles)
        {
            var role = await db.Roles.SingleAsync(r => r.Name == roleName);
            user.AssignRole(role.Id);
        }

        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    /// <summary>Seeds a user in the given role and logs in, returning a bearer token and the refresh credential.</summary>
    public static async Task<(User User, string AccessToken, string RefreshToken)> LoginAsRoleAsync(ApiFactory factory, HttpClient client, string role)
    {
        var user = await SeedUserAsync(factory, roles: role);
        var (login, refresh) = await LoginExpectingSessionAsync(client, user.UserName, DefaultPassword);
        return (user, login.Authenticated!.AccessToken, refresh);
    }

    public static Task<HttpResponseMessage> LoginAsync(HttpClient client, string userName, string password) =>
        client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(userName, password));

    public static async Task<(LoginResponse Body, string RefreshCookie)> LoginExpectingSessionAsync(HttpClient client, string userName, string password)
    {
        var response = await LoginAsync(client, userName, password);
        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        var body = (await response.Content.ReadFromJsonAsync<LoginResponse>(Json))!;
        body.MfaRequired.ShouldBeFalse();
        body.Authenticated.ShouldNotBeNull();

        return (body, ReadRefreshCookie(response)!);
    }

    public static string? ReadRefreshCookie(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            return null;
        }

        var header = cookies.FirstOrDefault(c => c.StartsWith(RefreshCookie.Name + "=", StringComparison.Ordinal));
        if (header is null)
        {
            return null;
        }

        var value = header.Split(';')[0][(RefreshCookie.Name.Length + 1)..];
        return string.IsNullOrEmpty(value) ? null : Uri.UnescapeDataString(value);
    }

    public static string? ReadRefreshCookieHeader(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var cookies)
            ? cookies.FirstOrDefault(c => c.StartsWith(RefreshCookie.Name + "=", StringComparison.Ordinal))
            : null;

    public static HttpRequestMessage WithRefreshCookie(this HttpRequestMessage request, string refreshToken)
    {
        request.Headers.Add("Cookie", $"{RefreshCookie.Name}={Uri.EscapeDataString(refreshToken)}");
        return request;
    }

    public static HttpRequestMessage WithBearer(this HttpRequestMessage request, string accessToken)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    public static Task<HttpResponseMessage> RefreshAsync(HttpClient client, string refreshToken) =>
        client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh").WithRefreshCookie(refreshToken));

    public static async Task<IReadOnlyList<AuditEvent>> AuditEventsAsync(ApiFactory factory, Guid? userId, string eventType)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        return await db.AuditEvents
            .Where(a => a.UserId == userId && a.EventType == eventType)
            .OrderBy(a => a.OccurredAtUtc)
            .ToListAsync();
    }

    public static async Task<User> ReloadUserAsync(ApiFactory factory, Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        return await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId);
    }

    /// <summary>Computes the code an authenticator application would show for the secret right now.</summary>
    public static string CurrentTotpCode(string secret, TimeProvider clock)
    {
        var step = (long)Math.Floor((clock.GetUtcNow() - DateTimeOffset.UnixEpoch).TotalSeconds / Rfc6238TotpService.StepSeconds);
        return Rfc6238TotpService.Compute(Base32.Decode(secret), step);
    }
}
