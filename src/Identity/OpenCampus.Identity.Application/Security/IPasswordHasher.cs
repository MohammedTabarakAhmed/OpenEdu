namespace OpenCampus.Identity.Application.Security;

/// <summary>Credential hashing contract (SEC-01). Implemented in the infrastructure layer.</summary>
public interface IPasswordHasher
{
    /// <summary>Produces a self-describing hash string carrying algorithm, iteration count, salt and derived key.</summary>
    string Hash(string password);

    /// <summary>Verifies a password against a stored hash in constant time.</summary>
    PasswordVerificationResult Verify(string password, string storedHash);
}

public enum PasswordVerificationResult
{
    Failed,
    Success,

    /// <summary>The password matched but the stored hash uses fewer iterations than currently configured and should be re-hashed.</summary>
    SuccessRehashNeeded,
}
