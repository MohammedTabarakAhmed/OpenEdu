namespace OpenCampus.Identity.Application.Security;

/// <summary>
/// Produces refresh credentials of at least 256 bits of entropy (SEC-05) and the
/// cryptographic hash under which they are persisted (SEC-06).
/// </summary>
public interface IRefreshTokenGenerator
{
    RefreshToken Generate();

    /// <summary>Hashes a presented credential so it can be matched against the stored hash.</summary>
    string Hash(string value);
}

/// <summary>The clear value is delivered to the browser once and never persisted; only <see cref="Hash"/> is stored.</summary>
public sealed record RefreshToken(string Value, string Hash);
