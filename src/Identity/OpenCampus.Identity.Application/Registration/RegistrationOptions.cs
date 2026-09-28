namespace OpenCampus.Identity.Application.Registration;

/// <summary>
/// Self-registration configuration (Increment 7; Appendix B style). The client base URL is the origin the verification
/// link points at — taken from configuration, never from the request's Host header, so a forged header cannot redirect
/// a registrant to an attacker's page.
/// </summary>
public sealed class RegistrationOptions
{
    public const string SectionName = "Registration";

    public static readonly TimeSpan MinimumLifetime = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan MaximumLifetime = TimeSpan.FromDays(7);

    /// <summary>When false the three anonymous registration endpoints answer 404 and nothing is created.</summary>
    public bool Enabled { get; set; } = true;

    public string ClientBaseUrl { get; set; } = "https://localhost:4200";

    public TimeSpan VerificationLifetime { get; set; } = TimeSpan.FromHours(24);

    public TimeSpan ResendCooldown { get; set; } = TimeSpan.FromSeconds(60);

    public bool Validate() =>
        Uri.TryCreate(ClientBaseUrl, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
        && VerificationLifetime >= MinimumLifetime
        && VerificationLifetime <= MaximumLifetime
        && ResendCooldown >= TimeSpan.Zero
        && ResendCooldown < VerificationLifetime;
}
