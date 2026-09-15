using OpenCampus.Identity.Application.Security;

namespace OpenCampus.UnitTests.Identity.Application;

public class SecurityOptionsTests
{
    private static TokenOptions ValidTokens() => new()
    {
        Issuer = "OpenCampus",
        Audience = "OpenCampus.Client",
        AccessTokenLifetime = TimeSpan.FromMinutes(15),
        RefreshTokenLifetime = TimeSpan.FromDays(14),
        SigningKeyPath = "keys/token-signing.pem",
    };

    [Fact]
    public void TokenOptions_AcceptsValuesAtTheMandatedCaps()
    {
        ValidTokens().Validate().ShouldBeTrue();
    }

    // SEC-03: access token lifetime shall not exceed 15 minutes.
    [Fact]
    public void TokenOptions_RejectsAccessLifetimeAboveFifteenMinutes()
    {
        var options = ValidTokens();
        options.AccessTokenLifetime = TimeSpan.FromMinutes(16);

        options.Validate().ShouldBeFalse();
    }

    // SEC-05: refresh credential lifetime shall not exceed 14 days.
    [Fact]
    public void TokenOptions_RejectsRefreshLifetimeAboveFourteenDays()
    {
        var options = ValidTokens();
        options.RefreshTokenLifetime = TimeSpan.FromDays(15);

        options.Validate().ShouldBeFalse();
    }

    [Theory]
    [InlineData("", "aud", "path")]
    [InlineData("iss", "", "path")]
    [InlineData("iss", "aud", " ")]
    public void TokenOptions_RejectsMissingIssuerAudienceOrKeyPath(string issuer, string audience, string path)
    {
        var options = ValidTokens();
        options.Issuer = issuer;
        options.Audience = audience;
        options.SigningKeyPath = path;

        options.Validate().ShouldBeFalse();
    }

    [Fact]
    public void TokenOptions_RejectsNonPositiveLifetimes()
    {
        var options = ValidTokens();
        options.AccessTokenLifetime = TimeSpan.Zero;
        options.Validate().ShouldBeFalse();

        options = ValidTokens();
        options.RefreshTokenLifetime = TimeSpan.FromDays(-1);
        options.Validate().ShouldBeFalse();
    }

    [Fact]
    public void AccountProtectionOptions_DefaultsAreValid()
    {
        new AccountProtectionOptions().Validate().ShouldBeTrue();
    }

    // SEC-14: threshold and duration are configurable but must be meaningful.
    [Fact]
    public void AccountProtectionOptions_RejectsZeroThresholdOrDuration()
    {
        new AccountProtectionOptions { LockoutThreshold = 0 }.Validate().ShouldBeFalse();
        new AccountProtectionOptions { LockoutDuration = TimeSpan.Zero }.Validate().ShouldBeFalse();
    }

    // SEC-16: rate limit window and permitted requests must be positive.
    [Fact]
    public void AccountProtectionOptions_RejectsInvalidRateLimit()
    {
        new AccountProtectionOptions { RateLimitWindow = TimeSpan.Zero }.Validate().ShouldBeFalse();
        new AccountProtectionOptions { RateLimitPermittedRequests = 0 }.Validate().ShouldBeFalse();
    }

    [Fact]
    public void PasswordHashingOptions_RejectsIterationsBelowFloor()
    {
        new PasswordHashingOptions().Validate().ShouldBeTrue();
        new PasswordHashingOptions { Iterations = PasswordHashingOptions.MinimumIterations }.Validate().ShouldBeTrue();
        new PasswordHashingOptions { Iterations = PasswordHashingOptions.MinimumIterations - 1 }.Validate().ShouldBeFalse();
    }
}
