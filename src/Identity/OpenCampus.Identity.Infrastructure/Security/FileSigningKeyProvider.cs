using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace OpenCampus.Identity.Infrastructure.Security;

/// <summary>
/// Loads the RSA signing key from a PEM file at the configured path, generating it on
/// first run. The file lives in a configured directory outside source control, so the
/// key is stable across host restarts (SEC-04, SDD 9.2).
/// </summary>
public sealed class FileSigningKeyProvider : ISigningKeyProvider
{
    public const int KeySizeBits = 2048;

    public FileSigningKeyProvider(string keyFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyFilePath);

        var rsa = LoadOrCreate(keyFilePath);
        var keyId = ComputeKeyId(rsa);

        SigningKey = new RsaSecurityKey(rsa) { KeyId = keyId };

        var publicOnly = RSA.Create();
        publicOnly.ImportRSAPublicKey(rsa.ExportRSAPublicKey(), out _);
        ValidationKey = new RsaSecurityKey(publicOnly) { KeyId = keyId };
    }

    public RsaSecurityKey SigningKey { get; }

    public RsaSecurityKey ValidationKey { get; }

    private static RSA LoadOrCreate(string path)
    {
        var rsa = RSA.Create(KeySizeBits);

        if (File.Exists(path))
        {
            rsa.ImportFromPem(File.ReadAllText(path));
            return rsa;
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, rsa.ExportRSAPrivateKeyPem());
        return rsa;
    }

    private static string ComputeKeyId(RSA rsa) =>
        Convert.ToHexStringLower(SHA256.HashData(rsa.ExportRSAPublicKey()))[..16];
}
