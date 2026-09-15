namespace OpenCampus.Api.Security;

/// <summary>
/// SEC-05: the refresh credential is delivered only in a cookie marked HttpOnly, Secure and
/// SameSite=Strict, scoped to the authentication routes so it is never sent elsewhere.
/// </summary>
public static class RefreshCookie
{
    public const string Name = "opencampus.refresh";
    public const string Path = "/api/v1/auth";

    public static void Set(HttpResponse response, string value, DateTime expiresAtUtc)
    {
        response.Cookies.Append(Name, value, Options(expiresAtUtc));
    }

    public static void Clear(HttpResponse response)
    {
        response.Cookies.Delete(Name, Options(DateTime.UnixEpoch));
    }

    public static string? Read(HttpRequest request) =>
        request.Cookies.TryGetValue(Name, out var value) && !string.IsNullOrEmpty(value) ? value : null;

    private static CookieOptions Options(DateTime expiresAtUtc) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = Path,
        Expires = new DateTimeOffset(expiresAtUtc, TimeSpan.Zero),
        IsEssential = true,
    };
}
