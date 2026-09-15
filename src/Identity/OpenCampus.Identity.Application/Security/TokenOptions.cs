namespace OpenCampus.Identity.Application.Security;

/// <summary>Token configuration (SDD Appendix B, "Tokens"). Bound from the "Tokens" section.</summary>
public sealed class TokenOptions
{
    public const string SectionName = "Tokens";

    /// <summary>Upper bound mandated by SEC-03.</summary>
    public static readonly TimeSpan MaxAccessTokenLifetime = TimeSpan.FromMinutes(15);

    /// <summary>Upper bound mandated by SEC-05.</summary>
    public static readonly TimeSpan MaxRefreshTokenLifetime = TimeSpan.FromDays(14);

    public string Issuer { get; set; } = string.Empty;

    public string Audience { get; set; } = string.Empty;

    public TimeSpan AccessTokenLifetime { get; set; } = MaxAccessTokenLifetime;

    public TimeSpan RefreshTokenLifetime { get; set; } = MaxRefreshTokenLifetime;

    /// <summary>Path of the RSA private key PEM, relative to the host content root (SEC-04).</summary>
    public string SigningKeyPath { get; set; } = string.Empty;

    /// <summary>Validity of the MFA challenge issued after the credential step (SEC-09).</summary>
    public TimeSpan MfaChallengeLifetime { get; set; } = TimeSpan.FromMinutes(5);

    public bool Validate()
    {
        return !string.IsNullOrWhiteSpace(Issuer)
            && !string.IsNullOrWhiteSpace(Audience)
            && !string.IsNullOrWhiteSpace(SigningKeyPath)
            && AccessTokenLifetime > TimeSpan.Zero
            && AccessTokenLifetime <= MaxAccessTokenLifetime
            && RefreshTokenLifetime > TimeSpan.Zero
            && RefreshTokenLifetime <= MaxRefreshTokenLifetime
            && MfaChallengeLifetime > TimeSpan.Zero
            && MfaChallengeLifetime <= MaxAccessTokenLifetime;
    }
}
