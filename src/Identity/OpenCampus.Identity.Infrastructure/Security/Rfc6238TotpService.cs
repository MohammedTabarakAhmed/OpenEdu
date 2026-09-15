using System.Security.Cryptography;
using System.Text;
using OpenCampus.Identity.Application.Security;

namespace OpenCampus.Identity.Infrastructure.Security;

/// <summary>
/// SEC-09: RFC 6238 time-based one-time passwords (HMAC-SHA1, 30-second step, 6 digits),
/// accepting codes from the adjacent steps to tolerate limited clock drift.
/// </summary>
internal sealed class Rfc6238TotpService : ITotpService
{
    public const int SecretSizeBytes = 20;
    public const int Digits = 6;
    public const int StepSeconds = 30;

    /// <summary>Steps accepted either side of the current one: ±30 seconds.</summary>
    public const int DriftSteps = 1;

    private static readonly DateTime UnixEpoch = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public string GenerateSecret() => Base32.Encode(RandomNumberGenerator.GetBytes(SecretSizeBytes));

    public string BuildProvisioningUri(string secret, string accountName, string issuer)
    {
        var label = Uri.EscapeDataString($"{issuer}:{accountName}");
        return $"otpauth://totp/{label}?secret={secret}&issuer={Uri.EscapeDataString(issuer)}&algorithm=SHA1&digits={Digits}&period={StepSeconds}";
    }

    public bool Verify(string secret, string code, DateTime utcNow)
    {
        if (string.IsNullOrEmpty(secret) || code is null || code.Length != Digits || !code.All(char.IsAsciiDigit))
        {
            return false;
        }

        byte[] key;
        try
        {
            key = Base32.Decode(secret);
        }
        catch (FormatException)
        {
            return false;
        }

        var currentStep = (long)Math.Floor((utcNow - UnixEpoch).TotalSeconds / StepSeconds);
        var expected = Encoding.ASCII.GetBytes(code);

        var matched = false;
        for (var offset = -DriftSteps; offset <= DriftSteps; offset++)
        {
            var candidate = Encoding.ASCII.GetBytes(Compute(key, currentStep + offset));
            matched |= CryptographicOperations.FixedTimeEquals(candidate, expected);
        }

        return matched;
    }

    internal static string Compute(byte[] key, long step)
    {
        Span<byte> counter = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(counter, step);

        Span<byte> hash = stackalloc byte[HMACSHA1.HashSizeInBytes];
        HMACSHA1.HashData(key, counter, hash);

        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24)
            | ((hash[offset + 1] & 0xFF) << 16)
            | ((hash[offset + 2] & 0xFF) << 8)
            | (hash[offset + 3] & 0xFF);

        return (binary % 1_000_000).ToString("D6");
    }
}

/// <summary>RFC 4648 Base32 without padding, as consumed by authenticator applications.</summary>
internal static class Base32
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string Encode(ReadOnlySpan<byte> data)
    {
        var output = new StringBuilder((data.Length * 8 + 4) / 5);
        var buffer = 0;
        var bits = 0;

        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                bits -= 5;
                output.Append(Alphabet[(buffer >> bits) & 0x1F]);
            }
        }

        if (bits > 0)
        {
            output.Append(Alphabet[(buffer << (5 - bits)) & 0x1F]);
        }

        return output.ToString();
    }

    public static byte[] Decode(string text)
    {
        var trimmed = text.TrimEnd('=').ToUpperInvariant();
        var output = new List<byte>(trimmed.Length * 5 / 8);
        var buffer = 0;
        var bits = 0;

        foreach (var c in trimmed)
        {
            var value = Alphabet.IndexOf(c);
            if (value < 0)
            {
                throw new FormatException("Invalid Base32 character.");
            }

            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                output.Add((byte)((buffer >> bits) & 0xFF));
            }
        }

        return [.. output];
    }
}
