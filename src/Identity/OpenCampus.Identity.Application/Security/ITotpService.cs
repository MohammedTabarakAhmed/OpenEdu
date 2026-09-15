namespace OpenCampus.Identity.Application.Security;

/// <summary>Time-based one-time passwords (SEC-09, RFC 6238). Implemented in infrastructure.</summary>
public interface ITotpService
{
    /// <summary>Generates a new Base32-encoded shared secret.</summary>
    string GenerateSecret();

    /// <summary>Builds the otpauth provisioning URI consumed by authenticator applications.</summary>
    string BuildProvisioningUri(string secret, string accountName, string issuer);

    /// <summary>Verifies a code, tolerating limited clock drift either side of the current step.</summary>
    bool Verify(string secret, string code, DateTime utcNow);
}
