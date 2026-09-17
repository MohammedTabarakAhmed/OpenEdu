using System.Security.Cryptography;
using OpenCampus.Sis.Application.Certificates;

namespace OpenCampus.Sis.Infrastructure.Certificates;

/// <summary>
/// Cryptographically random codes of the form XXXXX-XXXXX-XXXXX-XXXXX over an alphabet without look-alike characters
/// (no 0/O, 1/I/L), so a code read from a printed certificate can be typed back without ambiguity. 20 symbols of a
/// 31-symbol alphabet give roughly 99 bits of entropy — unguessable, and comfortably within the 32-character column.
/// </summary>
public sealed class VerificationCodeGenerator : IVerificationCodeGenerator
{
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    public string Next()
    {
        Span<char> buffer = stackalloc char[23];
        var position = 0;
        for (var group = 0; group < 4; group++)
        {
            if (group > 0)
            {
                buffer[position++] = '-';
            }

            for (var i = 0; i < 5; i++)
            {
                buffer[position++] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
            }
        }

        return new string(buffer);
    }
}
