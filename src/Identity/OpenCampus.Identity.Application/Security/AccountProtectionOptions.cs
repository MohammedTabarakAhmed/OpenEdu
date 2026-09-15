namespace OpenCampus.Identity.Application.Security;

/// <summary>Lockout and rate-limit configuration (SEC-14, SEC-16; SDD Appendix B, "Account protection").</summary>
public sealed class AccountProtectionOptions
{
    public const string SectionName = "AccountProtection";

    public int LockoutThreshold { get; set; } = 5;

    public TimeSpan LockoutDuration { get; set; } = TimeSpan.FromMinutes(15);

    public TimeSpan RateLimitWindow { get; set; } = TimeSpan.FromMinutes(1);

    public int RateLimitPermittedRequests { get; set; } = 10;

    public bool Validate()
    {
        return LockoutThreshold >= 1
            && LockoutDuration > TimeSpan.Zero
            && RateLimitWindow > TimeSpan.Zero
            && RateLimitPermittedRequests >= 1;
    }
}
