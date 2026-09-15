using Microsoft.IdentityModel.Tokens;

namespace OpenCampus.Identity.Infrastructure.Security;

/// <summary>Supplies the asymmetric key used to sign and validate access tokens (SEC-04).</summary>
public interface ISigningKeyProvider
{
    /// <summary>Private key for signing.</summary>
    RsaSecurityKey SigningKey { get; }

    /// <summary>Public key for validation; safe to hand to the authentication middleware.</summary>
    RsaSecurityKey ValidationKey { get; }
}
