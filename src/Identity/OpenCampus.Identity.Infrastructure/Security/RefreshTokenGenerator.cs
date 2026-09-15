using System.Security.Cryptography;
using System.Buffers.Text;
using OpenCampus.Identity.Application.Security;

namespace OpenCampus.Identity.Infrastructure.Security;

/// <summary>SEC-05: 256 bits of entropy, URL-safe. SEC-06: only the SHA-256 hash is persisted.</summary>
internal sealed class RefreshTokenGenerator : IRefreshTokenGenerator
{
    public const int EntropyBytes = 32;

    public RefreshToken Generate()
    {
        var value = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(EntropyBytes));
        return new RefreshToken(value, Hash(value));
    }

    public string Hash(string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(value);
        return Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)));
    }
}
