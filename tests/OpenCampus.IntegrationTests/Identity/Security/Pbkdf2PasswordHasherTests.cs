using Microsoft.Extensions.Options;
using OpenCampus.Identity.Application.Security;
using OpenCampus.Identity.Infrastructure.Security;

namespace OpenCampus.IntegrationTests.Identity.Security;

/// <summary>SEC-01. Pure computation; lives here because UnitTests may not reference Infrastructure (SDD 12).</summary>
public class Pbkdf2PasswordHasherTests
{
    // Low iteration counts keep the suite fast; the production floor is enforced by PasswordHashingOptions.
    private static Pbkdf2PasswordHasher Hasher(int iterations = 1_000) =>
        new(Options.Create(new PasswordHashingOptions { Iterations = iterations }));

    [Fact]
    public void Hash_ProducesSelfDescribingFormatWithSaltAndKeySizes()
    {
        var hash = Hasher(1_234).Hash("correct horse battery staple");

        var parts = hash.Split('$');
        parts.Length.ShouldBe(4);
        parts[0].ShouldBe(Pbkdf2PasswordHasher.AlgorithmTag);
        parts[1].ShouldBe("1234");
        Convert.FromBase64String(parts[2]).Length.ShouldBeGreaterThanOrEqualTo(16); // ≥ 128-bit salt
        Convert.FromBase64String(parts[3]).Length.ShouldBeGreaterThanOrEqualTo(32); // ≥ 256-bit key
    }

    [Fact]
    public void Hash_UsesFreshSaltEveryTime()
    {
        var hasher = Hasher();

        hasher.Hash("same").ShouldNotBe(hasher.Hash("same"));
    }

    [Fact]
    public void Verify_SucceedsForCorrectPassword()
    {
        var hasher = Hasher();
        var hash = hasher.Hash("pa55word!");

        hasher.Verify("pa55word!", hash).ShouldBe(PasswordVerificationResult.Success);
    }

    [Fact]
    public void Verify_FailsForWrongPassword()
    {
        var hasher = Hasher();
        var hash = hasher.Hash("pa55word!");

        hasher.Verify("pa55word?", hash).ShouldBe(PasswordVerificationResult.Failed);
        hasher.Verify("", hash).ShouldBe(PasswordVerificationResult.Failed);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-hash")]
    [InlineData("MD5$1000$c2FsdA==$aGFzaA==")]
    [InlineData("PBKDF2-SHA256$abc$c2FsdA==$aGFzaA==")]
    [InlineData("PBKDF2-SHA256$1000$not base64$aGFzaA==")]
    public void Verify_FailsWithoutThrowingForMalformedStoredHash(string stored)
    {
        Hasher().Verify("anything", stored).ShouldBe(PasswordVerificationResult.Failed);
    }

    [Fact]
    public void Verify_ReportsRehashNeededWhenStoredIterationsAreBelowConfigured()
    {
        var oldHash = Hasher(1_000).Hash("pa55word!");

        Hasher(2_000).Verify("pa55word!", oldHash).ShouldBe(PasswordVerificationResult.SuccessRehashNeeded);
    }

    [Fact]
    public void Hash_RejectsEmptyPassword()
    {
        Should.Throw<ArgumentException>(() => Hasher().Hash(""));
    }
}
