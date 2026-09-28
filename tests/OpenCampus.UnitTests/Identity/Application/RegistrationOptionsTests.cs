using OpenCampus.Identity.Application.Registration;

namespace OpenCampus.UnitTests.Identity.Application;

public class RegistrationOptionsTests
{
    [Fact]
    public void DefaultsAreValid()
    {
        var options = new RegistrationOptions();

        options.Enabled.ShouldBeTrue();
        options.VerificationLifetime.ShouldBe(TimeSpan.FromHours(24));
        options.ResendCooldown.ShouldBe(TimeSpan.FromSeconds(60));
        options.Validate().ShouldBeTrue();
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("ftp://host")]
    [InlineData("/relative")]
    public void RejectsUnusableClientBaseUrl(string url)
    {
        new RegistrationOptions { ClientBaseUrl = url }.Validate().ShouldBeFalse();
    }

    [Fact]
    public void BoundsLifetimeAndCooldown()
    {
        new RegistrationOptions { VerificationLifetime = TimeSpan.FromMinutes(4) }.Validate().ShouldBeFalse();
        new RegistrationOptions { VerificationLifetime = TimeSpan.FromMinutes(5) }.Validate().ShouldBeTrue();
        new RegistrationOptions { VerificationLifetime = TimeSpan.FromDays(7) }.Validate().ShouldBeTrue();
        new RegistrationOptions { VerificationLifetime = TimeSpan.FromDays(8) }.Validate().ShouldBeFalse();
        new RegistrationOptions { ResendCooldown = TimeSpan.FromSeconds(-1) }.Validate().ShouldBeFalse();
        new RegistrationOptions { ResendCooldown = TimeSpan.FromHours(24) }.Validate().ShouldBeFalse();
        new RegistrationOptions { ResendCooldown = TimeSpan.Zero }.Validate().ShouldBeTrue();
    }
}
