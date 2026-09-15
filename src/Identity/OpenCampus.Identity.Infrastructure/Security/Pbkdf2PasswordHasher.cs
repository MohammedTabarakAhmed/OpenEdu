using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using OpenCampus.Identity.Application.Security;

namespace OpenCampus.Identity.Infrastructure.Security;

/// <summary>
/// SEC-01: PBKDF2 with HMAC-SHA256, 128-bit random salt, 256-bit derived key, iteration
/// count persisted alongside the hash. Stored format:
/// <c>PBKDF2-SHA256$&lt;iterations&gt;$&lt;salt base64&gt;$&lt;key base64&gt;</c>.
/// </summary>
internal sealed class Pbkdf2PasswordHasher(IOptions<PasswordHashingOptions> options) : IPasswordHasher
{
    public const string AlgorithmTag = "PBKDF2-SHA256";
    public const int SaltSizeBytes = 16;
    public const int KeySizeBytes = 32;
    private const char Separator = '$';

    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);

        var iterations = options.Value.Iterations;
        var salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
        var key = Derive(password, salt, iterations);

        return string.Join(Separator, AlgorithmTag, iterations, Convert.ToBase64String(salt), Convert.ToBase64String(key));
    }

    public PasswordVerificationResult Verify(string password, string storedHash)
    {
        if (string.IsNullOrEmpty(password) || !TryParse(storedHash, out var iterations, out var salt, out var expectedKey))
        {
            return PasswordVerificationResult.Failed;
        }

        var actualKey = Derive(password, salt, iterations);
        if (!CryptographicOperations.FixedTimeEquals(actualKey, expectedKey))
        {
            return PasswordVerificationResult.Failed;
        }

        return iterations < options.Value.Iterations
            ? PasswordVerificationResult.SuccessRehashNeeded
            : PasswordVerificationResult.Success;
    }

    private static byte[] Derive(string password, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, KeySizeBytes);

    private static bool TryParse(string? storedHash, out int iterations, out byte[] salt, out byte[] key)
    {
        iterations = 0;
        salt = [];
        key = [];

        if (string.IsNullOrEmpty(storedHash))
        {
            return false;
        }

        var parts = storedHash.Split(Separator);
        if (parts.Length != 4 || parts[0] != AlgorithmTag || !int.TryParse(parts[1], out iterations) || iterations < 1)
        {
            return false;
        }

        try
        {
            salt = Convert.FromBase64String(parts[2]);
            key = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        return salt.Length >= SaltSizeBytes && key.Length == KeySizeBytes;
    }
}
