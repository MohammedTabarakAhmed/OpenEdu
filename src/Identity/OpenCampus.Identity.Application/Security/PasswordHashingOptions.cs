namespace OpenCampus.Identity.Application.Security;

/// <summary>
/// PBKDF2 work factor (SEC-01). The iteration count is stored with each hash so this
/// value can be raised later and existing hashes upgraded on next successful login.
/// </summary>
public sealed class PasswordHashingOptions
{
    public const string SectionName = "PasswordHashing";

    /// <summary>Floor below which the configuration is refused.</summary>
    public const int MinimumIterations = 100_000;

    /// <summary>OWASP (2023) recommendation for PBKDF2-HMAC-SHA256.</summary>
    public int Iterations { get; set; } = 600_000;

    public bool Validate() => Iterations >= MinimumIterations;
}
