using OpenCampus.SharedKernel;

namespace OpenCampus.Identity.Domain.Sessions;

/// <summary>
/// One refresh-credential generation. Rotation (SEC-07) revokes this row and creates a
/// successor that shares the same <see cref="FamilyId"/>; the family is the unit of
/// server-side revocation (SEC-08) and of reuse detection.
/// </summary>
public sealed class UserSession : Entity
{
    public const int RefreshTokenHashMaxLength = 128;
    public const int IpAddressMaxLength = 45;
    public const int UserAgentMaxLength = 512;

    private UserSession()
    {
    }

    public Guid UserId { get; private set; }

    public Guid FamilyId { get; private set; }

    public string RefreshTokenHash { get; private set; } = null!;

    public DateTime IssuedAtUtc { get; private set; }

    public DateTime ExpiresAtUtc { get; private set; }

    public DateTime? RevokedAtUtc { get; private set; }

    public string? IpAddress { get; private set; }

    public string? UserAgent { get; private set; }

    public bool IsRevoked => RevokedAtUtc is not null;

    public static UserSession Start(
        Guid userId,
        string refreshTokenHash,
        DateTime issuedAtUtc,
        TimeSpan lifetime,
        string? ipAddress,
        string? userAgent)
    {
        var session = Build(userId, refreshTokenHash, issuedAtUtc, lifetime, ipAddress, userAgent);
        session.FamilyId = session.Id;
        return session;
    }

    public bool IsUsable(DateTime utcNow) => !IsRevoked && ExpiresAtUtc > utcNow;

    /// <summary>Revokes this generation and returns its successor in the same family.</summary>
    public UserSession Rotate(
        string successorTokenHash,
        DateTime utcNow,
        TimeSpan lifetime,
        string? ipAddress,
        string? userAgent)
    {
        if (!IsUsable(utcNow))
        {
            throw new DomainException("A revoked or expired session cannot be rotated.");
        }

        Revoke(utcNow);

        var successor = Build(UserId, successorTokenHash, utcNow, lifetime, ipAddress, userAgent);
        successor.FamilyId = FamilyId;
        return successor;
    }

    public void Revoke(DateTime utcNow)
    {
        RevokedAtUtc ??= utcNow;
    }

    private static UserSession Build(
        Guid userId,
        string refreshTokenHash,
        DateTime issuedAtUtc,
        TimeSpan lifetime,
        string? ipAddress,
        string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(refreshTokenHash))
        {
            throw new DomainException("Refresh token hash is required.");
        }

        if (lifetime <= TimeSpan.Zero)
        {
            throw new DomainException("Session lifetime must be positive.");
        }

        return new UserSession
        {
            UserId = userId,
            RefreshTokenHash = refreshTokenHash,
            IssuedAtUtc = issuedAtUtc,
            ExpiresAtUtc = issuedAtUtc.Add(lifetime),
            IpAddress = Truncate(ipAddress, IpAddressMaxLength),
            UserAgent = Truncate(userAgent, UserAgentMaxLength),
        };
    }

    private static string? Truncate(string? value, int maxLength) =>
        value is null || value.Length <= maxLength ? value : value[..maxLength];
}
