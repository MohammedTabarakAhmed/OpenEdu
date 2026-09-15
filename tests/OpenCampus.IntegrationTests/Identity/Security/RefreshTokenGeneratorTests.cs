using System.Buffers.Text;
using OpenCampus.Identity.Infrastructure.Security;

namespace OpenCampus.IntegrationTests.Identity.Security;

public class RefreshTokenGeneratorTests
{
    private readonly RefreshTokenGenerator _generator = new();

    // SEC-05: at least 256 bits of entropy.
    [Fact]
    public void Generate_ProducesThirtyTwoRandomBytes()
    {
        var token = _generator.Generate();

        Base64Url.DecodeFromChars(token.Value).Length.ShouldBe(32);
        token.Value.ShouldNotBe(_generator.Generate().Value);
    }

    // SEC-06: the stored value is a hash, not the credential.
    [Fact]
    public void Hash_IsDeterministicAndDistinctFromValue()
    {
        var token = _generator.Generate();

        token.Hash.ShouldNotBe(token.Value);
        token.Hash.ShouldNotContain(token.Value);
        token.Hash.ShouldBe(_generator.Hash(token.Value));
        token.Hash.Length.ShouldBe(64); // SHA-256 hex, fits UserSession.RefreshTokenHashMaxLength
    }
}
