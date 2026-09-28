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
    public const int RoleNameMaxLength = 64;
    public const int VerificationTokenHashMaxLength = 128;

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

    public RegistrationStatus RegistrationStatus { get; private set; }

    /// <summary>Role name chosen at self-registration; never assigned until an administrator approves.</summary>
    public string? RequestedRole { get; private set; }

    public DateTime? EmailVerifiedAtUtc { get; private set; }

    /// <summary>SHA-256 of the verification token; the token itself is never stored.</summary>
    public string? VerificationTokenHash { get; private set; }

    public DateTime? VerificationTokenIssuedAtUtc { get; private set; }

    public DateTime? VerificationTokenExpiresAtUtc { get; private set; }

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

    /// <summary>
    /// Creates a self-registered account (Increment 7): inactive, no role, awaiting e-mail verification.
    /// The requested role is recorded as text only; <see cref="ApproveRegistration"/> is the sole path to it.
    /// </summary>
    public static User Register(
        string email,
        string userName,
        string passwordHash,
        string fullNameEn,
        string fullNameAr,
        string requestedRole)
    {
        return new User
        {
            Email = RequireText(email, nameof(email), EmailMaxLength),
            UserName = RequireText(userName, nameof(userName), UserNameMaxLength),
            PasswordHash = RequireText(passwordHash, nameof(passwordHash), int.MaxValue),
            FullNameEn = RequireText(fullNameEn, nameof(fullNameEn), FullNameMaxLength),
            FullNameAr = RequireText(fullNameAr, nameof(fullNameAr), FullNameMaxLength),
            RequestedRole = RequireText(requestedRole, nameof(requestedRole), RoleNameMaxLength),
            IsActive = false,
            RegistrationStatus = RegistrationStatus.AwaitingVerification,
        };
    }

    public bool IsLockedOut(DateTime utcNow) => LockedUntilUtc is { } until && until > utcNow;

    /// <summary>
    /// Stores a fresh verification token hash, replacing any earlier one. Refused inside the resend
    /// cooldown so the mailbox cannot be flooded; the cooldown does not apply to the first issue.
    /// </summary>
    public void IssueVerificationToken(string tokenHash, DateTime utcNow, TimeSpan lifetime, TimeSpan cooldown)
    {
        if (RegistrationStatus != RegistrationStatus.AwaitingVerification)
        {
            throw new DomainException("A verification token can only be issued while e-mail verification is pending.");
        }

        if (lifetime <= TimeSpan.Zero)
        {
            throw new DomainException("Verification token lifetime must be positive.");
        }

        if (VerificationTokenIssuedAtUtc is { } issued && issued.Add(cooldown) > utcNow)
        {
            throw new DomainException("A verification message was sent recently; wait before requesting another.");
        }

        VerificationTokenHash = RequireText(tokenHash, nameof(tokenHash), VerificationTokenHashMaxLength);
        VerificationTokenIssuedAtUtc = utcNow;
        VerificationTokenExpiresAtUtc = utcNow.Add(lifetime);
    }

    public bool IsVerificationTokenUsable(DateTime utcNow) =>
        VerificationTokenHash is not null && VerificationTokenExpiresAtUtc is { } expires && expires > utcNow;

    /// <summary>Consumes the verification token (single use) and moves the account to approval.</summary>
    public void ConfirmEmail(DateTime utcNow)
    {
        if (RegistrationStatus != RegistrationStatus.AwaitingVerification)
        {
            throw new DomainException("E-mail verification is not pending for this account.");
        }

        if (!IsVerificationTokenUsable(utcNow))
        {
            throw new DomainException("The verification token is missing or has expired.");
        }

        EmailVerifiedAtUtc = utcNow;
        VerificationTokenHash = null;
        VerificationTokenIssuedAtUtc = null;
        VerificationTokenExpiresAtUtc = null;
        RegistrationStatus = RegistrationStatus.AwaitingApproval;
    }

    /// <summary>Grants the role and activates the account; only reachable after the e-mail is verified.</summary>
    public void ApproveRegistration(Guid roleId)
    {
        if (RegistrationStatus != RegistrationStatus.AwaitingApproval)
        {
            throw new DomainException("Only a registration whose e-mail has been verified can be approved.");
        }

        AssignRole(roleId);
        RegistrationStatus = RegistrationStatus.Approved;
        IsActive = true;
    }

    /// <summary>True for an unverified registration whose token is absent or expired (a stale row that may be replaced).</summary>
    public bool IsRegistrationExpired(DateTime utcNow) =>
        RegistrationStatus == RegistrationStatus.AwaitingVerification && !IsVerificationTokenUsable(utcNow);

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
        if (RegistrationStatus is RegistrationStatus.AwaitingVerification or RegistrationStatus.AwaitingApproval)
        {
            throw new DomainException("A pending registration must be approved, not activated.");
        }

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
