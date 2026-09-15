using OpenCampus.SharedKernel;

namespace OpenCampus.Identity.Domain.Users;

/// <summary>
/// Aggregate root for a user account (SDD 13.2). Credential and lockout state
/// transitions occur only through the behaviour below (SDD 11.3).
/// </summary>
public sealed class User : Entity
{
    public const int EmailMaxLength = 256;
    public const int UserNameMaxLength = 64;
    public const int FullNameMaxLength = 200;

    private readonly List<UserRole> _roles = [];

    private User()
    {
    }

    public string Email { get; private set; } = null!;

    public string UserName { get; private set; } = null!;

    public string PasswordHash { get; private set; } = null!;

    public string FullNameEn { get; private set; } = null!;

    public string FullNameAr { get; private set; } = null!;

    public bool IsActive { get; private set; }

    public bool MfaEnabled { get; private set; }

    public string? MfaSecret { get; private set; }

    public int FailedLoginCount { get; private set; }

    public DateTime? LockedUntilUtc { get; private set; }

    public IReadOnlyCollection<UserRole> Roles => _roles.AsReadOnly();

    public static User Create(string email, string userName, string passwordHash, string fullNameEn, string fullNameAr)
    {
        return new User
        {
            Email = RequireText(email, nameof(email), EmailMaxLength),
            UserName = RequireText(userName, nameof(userName), UserNameMaxLength),
            PasswordHash = RequireText(passwordHash, nameof(passwordHash), int.MaxValue),
            FullNameEn = RequireText(fullNameEn, nameof(fullNameEn), FullNameMaxLength),
            FullNameAr = RequireText(fullNameAr, nameof(fullNameAr), FullNameMaxLength),
            IsActive = true,
        };
    }

    public bool IsLockedOut(DateTime utcNow) => LockedUntilUtc is { } until && until > utcNow;

    /// <summary>
    /// Records a failed credential check (SEC-14). Returns true when this failure
    /// crossed the threshold and triggered a lockout.
    /// </summary>
    public bool RecordFailedLogin(DateTime utcNow, int lockoutThreshold, TimeSpan lockoutDuration)
    {
        if (lockoutThreshold < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(lockoutThreshold), "Lockout threshold must be at least 1.");
        }

        if (LockedUntilUtc is { } until && until <= utcNow)
        {
            // A previous lockout has expired; start counting afresh.
            LockedUntilUtc = null;
            FailedLoginCount = 0;
        }

        FailedLoginCount++;

        if (FailedLoginCount < lockoutThreshold)
        {
            return false;
        }

        LockedUntilUtc = utcNow.Add(lockoutDuration);
        return true;
    }

    public void RecordSuccessfulLogin()
    {
        FailedLoginCount = 0;
        LockedUntilUtc = null;
    }

    public void ChangePassword(string newPasswordHash)
    {
        PasswordHash = RequireText(newPasswordHash, nameof(newPasswordHash), int.MaxValue);
    }

    /// <summary>Stores a pending shared secret; MFA is not enforced until <see cref="ConfirmMfaEnrolment"/>.</summary>
    public void BeginMfaEnrolment(string secret)
    {
        if (MfaEnabled)
        {
            throw new DomainException("Multi-factor authentication is already enabled.");
        }

        MfaSecret = RequireText(secret, nameof(secret), int.MaxValue);
    }

    /// <summary>Enforces MFA once the user has proven possession of the pending secret.</summary>
    public void ConfirmMfaEnrolment()
    {
        if (MfaSecret is null)
        {
            throw new DomainException("No multi-factor enrolment is pending.");
        }

        MfaEnabled = true;
    }

    public void DisableMfa()
    {
        MfaEnabled = false;
        MfaSecret = null;
    }

    public void Amend(string email, string fullNameEn, string fullNameAr)
    {
        Email = RequireText(email, nameof(email), EmailMaxLength);
        FullNameEn = RequireText(fullNameEn, nameof(fullNameEn), FullNameMaxLength);
        FullNameAr = RequireText(fullNameAr, nameof(fullNameAr), FullNameMaxLength);
    }

    public void Deactivate()
    {
        IsActive = false;
    }

    public void Activate()
    {
        IsActive = true;
    }

    /// <summary>Assigns a role; idempotent. Returns true when the assignment was new.</summary>
    public bool AssignRole(Guid roleId)
    {
        if (_roles.Any(r => r.RoleId == roleId))
        {
            return false;
        }

        _roles.Add(new UserRole(Id, roleId));
        return true;
    }

    public bool RemoveRole(Guid roleId)
    {
        return _roles.RemoveAll(r => r.RoleId == roleId) > 0;
    }

    private static string RequireText(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException($"{name} is required.");
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new DomainException($"{name} must not exceed {maxLength} characters.");
        }

        return trimmed;
    }
}
