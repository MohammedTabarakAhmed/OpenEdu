using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using OpenCampus.Identity.Application.Security;

namespace OpenCampus.Identity.Infrastructure.Security;

/// <summary>
/// SEC-09: the challenge is an encrypted, authenticated, time-limited payload bound to one
/// user. It carries no roles or permissions and is not accepted by the bearer scheme.
/// </summary>
internal sealed class DataProtectionMfaChallengeIssuer : IMfaChallengeIssuer
{
    public const string Purpose = "OpenCampus.Identity.MfaChallenge";

    private readonly ITimeLimitedDataProtector _protector;
    private readonly TimeSpan _lifetime;

    public DataProtectionMfaChallengeIssuer(IDataProtectionProvider provider, IOptions<TokenOptions> options)
    {
        _protector = provider.CreateProtector(Purpose).ToTimeLimitedDataProtector();
        _lifetime = options.Value.MfaChallengeLifetime;
    }

    public string Issue(Guid userId)
    {
        // A random nonce makes every challenge unique even for the same user and instant.
        var payload = $"{userId:N}:{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16))}";
        return _protector.Protect(payload, _lifetime);
    }

    public Guid? Validate(string challenge)
    {
        if (string.IsNullOrWhiteSpace(challenge))
        {
            return null;
        }

        try
        {
            var payload = _protector.Unprotect(challenge);
            var separator = payload.IndexOf(':');
            return separator > 0 && Guid.TryParseExact(payload[..separator], "N", out var userId) ? userId : null;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}
