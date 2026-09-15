using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using OpenCampus.Identity.Application.Security;
using OpenCampus.Identity.Infrastructure.Security;

namespace OpenCampus.IntegrationTests.Identity.Security;

/// <summary>SEC-09. Pure computation; lives here because UnitTests may not reference Infrastructure (SDD 12).</summary>
public class Rfc6238TotpServiceTests
{
    private static readonly DateTime Now = new(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc);
    private readonly Rfc6238TotpService _totp = new();

    // RFC 6238 Appendix B vectors (SHA-1, secret "12345678901234567890"), truncated to 6 digits.
    [Theory]
    [InlineData(59L, "287082")]
    [InlineData(1111111109L, "081804")]
    [InlineData(1234567890L, "005924")]
    [InlineData(20000000000L, "353130")]
    public void Compute_MatchesRfc6238TestVectors(long unixSeconds, string expected)
    {
        var key = Encoding.ASCII.GetBytes("12345678901234567890");

        Rfc6238TotpService.Compute(key, unixSeconds / Rfc6238TotpService.StepSeconds).ShouldBe(expected);
    }

    [Fact]
    public void GenerateSecret_IsBase32OfTwentyRandomBytes()
    {
        var secret = _totp.GenerateSecret();

        secret.ShouldMatch("^[A-Z2-7]+$");
        Base32.Decode(secret).Length.ShouldBe(Rfc6238TotpService.SecretSizeBytes);
        secret.ShouldNotBe(_totp.GenerateSecret());
    }

    [Fact]
    public void Base32_RoundTrips()
    {
        var bytes = new byte[] { 0, 1, 2, 3, 250, 251, 252, 253, 254, 255, 42 };

        Base32.Decode(Base32.Encode(bytes)).ShouldBe(bytes);
        Should.Throw<FormatException>(() => Base32.Decode("not*base32"));
    }

    [Fact]
    public void Verify_AcceptsCurrentAndAdjacentSteps_RejectsBeyondDrift()
    {
        var secret = _totp.GenerateSecret();
        var key = Base32.Decode(secret);
        var step = (long)Math.Floor((Now - DateTime.UnixEpoch).TotalSeconds / Rfc6238TotpService.StepSeconds);

        _totp.Verify(secret, Rfc6238TotpService.Compute(key, step), Now).ShouldBeTrue();
        _totp.Verify(secret, Rfc6238TotpService.Compute(key, step - 1), Now).ShouldBeTrue("one step of drift behind");
        _totp.Verify(secret, Rfc6238TotpService.Compute(key, step + 1), Now).ShouldBeTrue("one step of drift ahead");
        _totp.Verify(secret, Rfc6238TotpService.Compute(key, step - 2), Now).ShouldBeFalse("two steps behind is outside the window");
        _totp.Verify(secret, Rfc6238TotpService.Compute(key, step + 2), Now).ShouldBeFalse("two steps ahead is outside the window");
    }

    [Theory]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("12345a")]
    public void Verify_RejectsMalformedCodes(string code)
    {
        _totp.Verify(_totp.GenerateSecret(), code, Now).ShouldBeFalse();
    }

    [Fact]
    public void Verify_RejectsMalformedSecretWithoutThrowing()
    {
        _totp.Verify("not*base32", "123456", Now).ShouldBeFalse();
        _totp.Verify("", "123456", Now).ShouldBeFalse();
    }

    [Fact]
    public void ProvisioningUri_CarriesIssuerAccountAndSecret()
    {
        var uri = _totp.BuildProvisioningUri("JBSWY3DPEHPK3PXP", "ada", "OpenCampus");

        uri.ShouldBe("otpauth://totp/OpenCampus%3Aada?secret=JBSWY3DPEHPK3PXP&issuer=OpenCampus&algorithm=SHA1&digits=6&period=30");
    }
}

public class DataProtectionMfaChallengeIssuerTests
{
    private static DataProtectionMfaChallengeIssuer Issuer(TimeSpan lifetime) =>
        new(new EphemeralDataProtectionProvider(), Options.Create(new TokenOptions { MfaChallengeLifetime = lifetime }));

    [Fact]
    public void Challenge_RoundTripsToBoundUser()
    {
        var issuer = Issuer(TimeSpan.FromMinutes(5));
        var userId = Guid.NewGuid();

        var challenge = issuer.Issue(userId);

        challenge.ShouldNotContain(userId.ToString("N"));
        issuer.Validate(challenge).ShouldBe(userId);
        issuer.Issue(userId).ShouldNotBe(challenge);
    }

    [Fact]
    public void Challenge_IsRejectedWhenTamperedOrForeign()
    {
        var issuer = Issuer(TimeSpan.FromMinutes(5));
        var challenge = issuer.Issue(Guid.NewGuid());

        issuer.Validate(challenge[..^2] + "zz").ShouldBeNull();
        issuer.Validate("").ShouldBeNull();
        Issuer(TimeSpan.FromMinutes(5)).Validate(challenge).ShouldBeNull("a different key ring must not accept it");
    }

    [Fact]
    public async Task Challenge_ExpiresAfterLifetime()
    {
        var issuer = Issuer(TimeSpan.FromMilliseconds(200));
        var challenge = issuer.Issue(Guid.NewGuid());

        issuer.Validate(challenge).ShouldNotBeNull();
        await Task.Delay(400);
        issuer.Validate(challenge).ShouldBeNull();
    }
}
