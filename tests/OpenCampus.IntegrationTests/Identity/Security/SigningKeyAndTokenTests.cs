using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using OpenCampus.Identity.Application.Security;
using OpenCampus.Identity.Infrastructure.Security;

namespace OpenCampus.IntegrationTests.Identity.Security;

public class FileSigningKeyProviderTests
{
    // SEC-04: the key is created outside the repository and is stable across restarts.
    [Fact]
    public void Key_IsCreatedOnFirstUseAndReloadedIdentically()
    {
        var directory = Path.Combine(Path.GetTempPath(), "opencampus-tests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "token-signing.pem");

        try
        {
            File.Exists(path).ShouldBeFalse();

            var first = new FileSigningKeyProvider(path);
            File.Exists(path).ShouldBeTrue();
            File.ReadAllText(path).ShouldStartWith("-----BEGIN RSA PRIVATE KEY-----");

            var second = new FileSigningKeyProvider(path);

            second.KeyIdOf().ShouldBe(first.KeyIdOf());
            second.ValidationKey.Rsa.ExportRSAPublicKey().ShouldBe(first.ValidationKey.Rsa.ExportRSAPublicKey());
            first.SigningKey.Rsa.KeySize.ShouldBeGreaterThanOrEqualTo(FileSigningKeyProvider.KeySizeBits);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void ValidationKey_CarriesNoPrivateParameters()
    {
        var path = Path.Combine(Path.GetTempPath(), "opencampus-tests", Guid.NewGuid().ToString("N"), "k.pem");

        try
        {
            var provider = new FileSigningKeyProvider(path);

            Should.Throw<System.Security.Cryptography.CryptographicException>(() => provider.ValidationKey.Rsa.ExportRSAPrivateKey());
            provider.SigningKey.Rsa.ExportRSAPrivateKey().Length.ShouldBeGreaterThan(0);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
        }
    }
}

[Collection(ApiCollection.Name)]
public class JwtAccessTokenIssuerTests(ApiFactory factory)
{
    private static readonly AccessTokenRequest Request = new(
        UserId: Guid.NewGuid(),
        UserName: "ada",
        SessionId: Guid.NewGuid(),
        Roles: ["Instructor"],
        Permissions: ["lms.content.read", "lms.content.write"]);

    [Fact]
    public async Task IssuedToken_IsRs256SignedAndValidatesAgainstPublicKey()
    {
        var issuer = factory.Services.GetRequiredService<IAccessTokenIssuer>();
        var keys = factory.Services.GetRequiredService<ISigningKeyProvider>();
        var options = factory.Services.GetRequiredService<IOptions<TokenOptions>>().Value;

        var token = issuer.Issue(Request);

        var handler = new JsonWebTokenHandler();
        var result = await handler.ValidateTokenAsync(token.Value, new TokenValidationParameters
        {
            ValidIssuer = options.Issuer,
            ValidAudience = options.Audience,
            IssuerSigningKey = keys.ValidationKey,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            ClockSkew = TimeSpan.Zero,
        });

        result.IsValid.ShouldBeTrue(result.Exception?.ToString());
        var jwt = (JsonWebToken)result.SecurityToken;
        jwt.Alg.ShouldBe(SecurityAlgorithms.RsaSha256);
        jwt.Subject.ShouldBe(Request.UserId.ToString());
        jwt.GetClaim(JwtAccessTokenIssuer.SessionClaim).Value.ShouldBe(Request.SessionId.ToString());
        jwt.Claims.Where(c => c.Type == JwtAccessTokenIssuer.RoleClaim).Select(c => c.Value).ShouldBe(["Instructor"]);
        jwt.Claims.Where(c => c.Type == JwtAccessTokenIssuer.PermissionClaim).Select(c => c.Value)
            .ShouldBe(["lms.content.read", "lms.content.write"]);
    }

    // SEC-03: lifetime not exceeding 15 minutes.
    [Fact]
    public void IssuedToken_ExpiresWithinFifteenMinutes()
    {
        var issuer = factory.Services.GetRequiredService<IAccessTokenIssuer>();

        var token = issuer.Issue(Request);

        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(token.Value);
        (jwt.ValidTo - jwt.IssuedAt).ShouldBeLessThanOrEqualTo(TimeSpan.FromMinutes(15));
        (jwt.ValidTo - jwt.IssuedAt).ShouldBeGreaterThan(TimeSpan.Zero);
        token.ExpiresAtUtc.ShouldBe(jwt.ValidTo, tolerance: TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task IssuedToken_IsRejectedWhenSignedByAnotherKey()
    {
        var issuer = factory.Services.GetRequiredService<IAccessTokenIssuer>();
        var options = factory.Services.GetRequiredService<IOptions<TokenOptions>>().Value;
        var otherPath = Path.Combine(Path.GetTempPath(), "opencampus-tests", Guid.NewGuid().ToString("N"), "other.pem");

        try
        {
            var otherKeys = new FileSigningKeyProvider(otherPath);
            var token = issuer.Issue(Request);

            var result = await new JsonWebTokenHandler().ValidateTokenAsync(token.Value, new TokenValidationParameters
            {
                ValidIssuer = options.Issuer,
                ValidAudience = options.Audience,
                IssuerSigningKey = otherKeys.ValidationKey,
            });

            result.IsValid.ShouldBeFalse();
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(otherPath)!, recursive: true);
        }
    }

    [Fact]
    public void Host_PersistsSigningKeyAtConfiguredPath()
    {
        var options = factory.Services.GetRequiredService<IOptions<TokenOptions>>().Value;
        _ = factory.Services.GetRequiredService<ISigningKeyProvider>();

        var expected = Path.Combine(factory.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>().ContentRootPath, options.SigningKeyPath);
        File.Exists(expected).ShouldBeTrue();
    }
}

internal static class SigningKeyProviderExtensions
{
    public static string? KeyIdOf(this ISigningKeyProvider provider) => provider.SigningKey.KeyId;
}
