using OpenCampus.Identity.Domain.Users;
using OpenCampus.SharedKernel;

namespace OpenCampus.UnitTests.Identity.Domain;

public class UserTests
{
    private static readonly DateTime Now = new(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    private static User NewUser() =>
        User.Create("ada@example.org", "ada", "hash", "Ada Lovelace", "آدا لوفلايس");

    [Fact]
    public void Create_TrimsAndActivates()
    {
        var user = User.Create("  ada@example.org ", " ada ", "hash", " Ada ", " آدا ");

        user.Email.ShouldBe("ada@example.org");
        user.UserName.ShouldBe("ada");
        user.FullNameEn.ShouldBe("Ada");
        user.FullNameAr.ShouldBe("آدا");
        user.IsActive.ShouldBeTrue();
        user.MfaEnabled.ShouldBeFalse();
        user.FailedLoginCount.ShouldBe(0);
        user.LockedUntilUtc.ShouldBeNull();
    }

    [Theory]
    [InlineData("", "ada", "hash", "Ada", "آدا")]
    [InlineData("ada@example.org", " ", "hash", "Ada", "آدا")]
    [InlineData("ada@example.org", "ada", "", "Ada", "آدا")]
    [InlineData("ada@example.org", "ada", "hash", "", "آدا")]
    [InlineData("ada@example.org", "ada", "hash", "Ada", "")]
    public void Create_RejectsMissingRequiredText(string email, string userName, string hash, string en, string ar)
    {
        Should.Throw<DomainException>(() => User.Create(email, userName, hash, en, ar));
    }

    [Fact]
    public void Create_RejectsOverlongUserName()
    {
        var overlong = new string('a', User.UserNameMaxLength + 1);

        Should.Throw<DomainException>(() => User.Create("ada@example.org", overlong, "hash", "Ada", "آدا"));
    }

    // SEC-14: consecutive failures trigger temporary lockout.
    [Fact]
    public void RecordFailedLogin_BelowThreshold_DoesNotLock()
    {
        var user = NewUser();

        user.RecordFailedLogin(Now, lockoutThreshold: 3, LockoutDuration).ShouldBeFalse();
        user.RecordFailedLogin(Now, lockoutThreshold: 3, LockoutDuration).ShouldBeFalse();

        user.FailedLoginCount.ShouldBe(2);
        user.IsLockedOut(Now).ShouldBeFalse();
    }

    [Fact]
    public void RecordFailedLogin_AtThreshold_LocksForConfiguredDuration()
    {
        var user = NewUser();
        user.RecordFailedLogin(Now, 3, LockoutDuration);
        user.RecordFailedLogin(Now, 3, LockoutDuration);

        user.RecordFailedLogin(Now, 3, LockoutDuration).ShouldBeTrue();

        user.LockedUntilUtc.ShouldBe(Now.Add(LockoutDuration));
        user.IsLockedOut(Now).ShouldBeTrue();
        user.IsLockedOut(Now.Add(LockoutDuration).AddSeconds(-1)).ShouldBeTrue();
        user.IsLockedOut(Now.Add(LockoutDuration)).ShouldBeFalse();
    }

    [Fact]
    public void RecordFailedLogin_AfterLockoutExpires_RestartsCount()
    {
        var user = NewUser();
        for (var i = 0; i < 3; i++)
        {
            user.RecordFailedLogin(Now, 3, LockoutDuration);
        }

        var later = Now.Add(LockoutDuration).AddMinutes(1);
        user.RecordFailedLogin(later, 3, LockoutDuration).ShouldBeFalse();

        user.FailedLoginCount.ShouldBe(1);
        user.IsLockedOut(later).ShouldBeFalse();
    }

    [Fact]
    public void RecordFailedLogin_RejectsThresholdBelowOne()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => NewUser().RecordFailedLogin(Now, 0, LockoutDuration));
    }

    [Fact]
    public void RecordSuccessfulLogin_ClearsFailuresAndLock()
    {
        var user = NewUser();
        for (var i = 0; i < 3; i++)
        {
            user.RecordFailedLogin(Now, 3, LockoutDuration);
        }

        user.RecordSuccessfulLogin();

        user.FailedLoginCount.ShouldBe(0);
        user.LockedUntilUtc.ShouldBeNull();
        user.IsLockedOut(Now).ShouldBeFalse();
    }

    // SEC-09: enrolment is two-phase; the credential step only enforces MFA after confirmation.
    [Fact]
    public void BeginMfaEnrolment_StoresPendingSecretWithoutEnabling()
    {
        var user = NewUser();

        user.BeginMfaEnrolment("secret");

        user.MfaEnabled.ShouldBeFalse();
        user.MfaSecret.ShouldBe("secret");
    }

    [Fact]
    public void ConfirmMfaEnrolment_EnablesMfa()
    {
        var user = NewUser();
        user.BeginMfaEnrolment("secret");

        user.ConfirmMfaEnrolment();

        user.MfaEnabled.ShouldBeTrue();
    }

    [Fact]
    public void ConfirmMfaEnrolment_RefusedWithoutPendingSecret()
    {
        Should.Throw<DomainException>(() => NewUser().ConfirmMfaEnrolment());
    }

    [Fact]
    public void BeginMfaEnrolment_RefusedWhenAlreadyEnabledOrSecretEmpty()
    {
        var user = NewUser();
        user.BeginMfaEnrolment("secret");
        user.ConfirmMfaEnrolment();

        Should.Throw<DomainException>(() => user.BeginMfaEnrolment("other"));
        Should.Throw<DomainException>(() => NewUser().BeginMfaEnrolment(" "));
    }

    [Fact]
    public void DisableMfa_ClearsSecret()
    {
        var user = NewUser();
        user.BeginMfaEnrolment("secret");
        user.ConfirmMfaEnrolment();

        user.DisableMfa();

        user.MfaEnabled.ShouldBeFalse();
        user.MfaSecret.ShouldBeNull();
    }

    [Fact]
    public void ChangePassword_RejectsEmptyHash()
    {
        Should.Throw<DomainException>(() => NewUser().ChangePassword(""));
    }

    [Fact]
    public void Deactivate_ThenActivate_TogglesIsActive()
    {
        var user = NewUser();

        user.Deactivate();
        user.IsActive.ShouldBeFalse();

        user.Activate();
        user.IsActive.ShouldBeTrue();
    }

    [Fact]
    public void AssignRole_IsIdempotent()
    {
        var user = NewUser();
        var roleId = Guid.NewGuid();

        user.AssignRole(roleId).ShouldBeTrue();
        user.AssignRole(roleId).ShouldBeFalse();

        user.Roles.Count.ShouldBe(1);
        user.Roles.Single().UserId.ShouldBe(user.Id);
        user.Roles.Single().RoleId.ShouldBe(roleId);
    }

    [Fact]
    public void RemoveRole_ReportsWhetherAssignmentExisted()
    {
        var user = NewUser();
        var roleId = Guid.NewGuid();
        user.AssignRole(roleId);

        user.RemoveRole(roleId).ShouldBeTrue();
        user.RemoveRole(roleId).ShouldBeFalse();
        user.Roles.ShouldBeEmpty();
    }

    // Increment 7: self-registration lifecycle.
    private static User NewRegistrant(string role = "Instructor") =>
        User.Register("new@example.org", "newbie", "hash", "New Person", "شخص جديد", role);

    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);
    private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(60);

    [Fact]
    public void Register_IsInactiveAwaitingVerification()
    {
        var user = User.Register(" new@example.org ", " newbie ", "hash", " New ", " جديد ", " Learner ");

        user.IsActive.ShouldBeFalse();
        user.RegistrationStatus.ShouldBe(RegistrationStatus.AwaitingVerification);
        user.RequestedRole.ShouldBe("Learner");
        user.Roles.ShouldBeEmpty();
        user.EmailVerifiedAtUtc.ShouldBeNull();
        user.VerificationTokenHash.ShouldBeNull();
        user.IsVerificationTokenUsable(Now).ShouldBeFalse();
    }

    [Fact]
    public void Register_RequiresRequestedRole()
    {
        Should.Throw<DomainException>(() => User.Register("a@b.c", "a", "hash", "A", "أ", " "));
        Should.Throw<DomainException>(() =>
            User.Register("a@b.c", "a", "hash", "A", "أ", new string('r', User.RoleNameMaxLength + 1)));
    }

    [Fact]
    public void Create_HasNoRegistrationState()
    {
        var user = NewUser();

        user.RegistrationStatus.ShouldBe(RegistrationStatus.None);
        user.RequestedRole.ShouldBeNull();
    }

    [Fact]
    public void IssueVerificationToken_SetsHashAndExpiry()
    {
        var user = NewRegistrant();

        user.IssueVerificationToken("h1", Now, Lifetime, Cooldown);

        user.VerificationTokenHash.ShouldBe("h1");
        user.VerificationTokenIssuedAtUtc.ShouldBe(Now);
        user.VerificationTokenExpiresAtUtc.ShouldBe(Now.Add(Lifetime));
        user.IsVerificationTokenUsable(Now).ShouldBeTrue();
        user.IsVerificationTokenUsable(Now.Add(Lifetime)).ShouldBeFalse();
    }

    [Fact]
    public void IssueVerificationToken_RespectsCooldown()
    {
        var user = NewRegistrant();
        user.IssueVerificationToken("h1", Now, Lifetime, Cooldown);

        Should.Throw<DomainException>(() => user.IssueVerificationToken("h2", Now.AddSeconds(30), Lifetime, Cooldown));
        user.VerificationTokenHash.ShouldBe("h1");

        user.IssueVerificationToken("h2", Now.Add(Cooldown), Lifetime, Cooldown);
        user.VerificationTokenHash.ShouldBe("h2");
        user.VerificationTokenExpiresAtUtc.ShouldBe(Now.Add(Cooldown).Add(Lifetime));
    }

    [Fact]
    public void IssueVerificationToken_RejectsBadInput()
    {
        var user = NewRegistrant();

        Should.Throw<DomainException>(() => user.IssueVerificationToken("h1", Now, TimeSpan.Zero, Cooldown));
        Should.Throw<DomainException>(() => user.IssueVerificationToken(" ", Now, Lifetime, Cooldown));
        Should.Throw<DomainException>(() => NewUser().IssueVerificationToken("h1", Now, Lifetime, Cooldown));
    }

    [Fact]
    public void ConfirmEmail_RequiresUsableToken_ClearsTokenAndMovesToAwaitingApproval()
    {
        var user = NewRegistrant();
        Should.Throw<DomainException>(() => user.ConfirmEmail(Now));

        user.IssueVerificationToken("h1", Now, Lifetime, Cooldown);
        Should.Throw<DomainException>(() => user.ConfirmEmail(Now.Add(Lifetime)));

        user.ConfirmEmail(Now.AddMinutes(5));

        user.RegistrationStatus.ShouldBe(RegistrationStatus.AwaitingApproval);
        user.EmailVerifiedAtUtc.ShouldBe(Now.AddMinutes(5));
        user.VerificationTokenHash.ShouldBeNull();
        user.VerificationTokenIssuedAtUtc.ShouldBeNull();
        user.VerificationTokenExpiresAtUtc.ShouldBeNull();
        user.IsActive.ShouldBeFalse();
        user.Roles.ShouldBeEmpty();

        // Single use: a second confirmation is refused and no new token can be issued.
        Should.Throw<DomainException>(() => user.ConfirmEmail(Now.AddMinutes(6)));
        Should.Throw<DomainException>(() => user.IssueVerificationToken("h2", Now.AddHours(1), Lifetime, Cooldown));
    }

    [Fact]
    public void ApproveRegistration_RequiresVerifiedEmail()
    {
        var user = NewRegistrant();
        user.IssueVerificationToken("h1", Now, Lifetime, Cooldown);

        Should.Throw<DomainException>(() => user.ApproveRegistration(Guid.NewGuid()));
        Should.Throw<DomainException>(() => NewUser().ApproveRegistration(Guid.NewGuid()));

        user.Roles.ShouldBeEmpty();
        user.IsActive.ShouldBeFalse();
    }

    [Fact]
    public void ApproveRegistration_AssignsRoleAndActivates()
    {
        var user = NewRegistrant();
        user.IssueVerificationToken("h1", Now, Lifetime, Cooldown);
        user.ConfirmEmail(Now);
        var roleId = Guid.NewGuid();

        user.ApproveRegistration(roleId);

        user.RegistrationStatus.ShouldBe(RegistrationStatus.Approved);
        user.IsActive.ShouldBeTrue();
        user.Roles.Single().RoleId.ShouldBe(roleId);

        Should.Throw<DomainException>(() => user.ApproveRegistration(roleId));
        user.Deactivate();
        user.Activate();
        user.IsActive.ShouldBeTrue();
    }

    [Fact]
    public void Activate_RefusedWhilePending()
    {
        var awaitingVerification = NewRegistrant();
        Should.Throw<DomainException>(awaitingVerification.Activate);
        awaitingVerification.IsActive.ShouldBeFalse();

        var awaitingApproval = NewRegistrant();
        awaitingApproval.IssueVerificationToken("h1", Now, Lifetime, Cooldown);
        awaitingApproval.ConfirmEmail(Now);
        Should.Throw<DomainException>(awaitingApproval.Activate);
        awaitingApproval.IsActive.ShouldBeFalse();
        awaitingApproval.Roles.ShouldBeEmpty();
    }

    [Fact]
    public void IsRegistrationExpired_TrueWithoutOrPastToken()
    {
        var user = NewRegistrant();
        user.IsRegistrationExpired(Now).ShouldBeTrue();

        user.IssueVerificationToken("h1", Now, Lifetime, Cooldown);
        user.IsRegistrationExpired(Now.AddHours(1)).ShouldBeFalse();
        user.IsRegistrationExpired(Now.Add(Lifetime)).ShouldBeTrue();
    }

    [Fact]
    public void IsRegistrationExpired_FalseOnceVerifiedOrForAdminCreatedUsers()
    {
        var user = NewRegistrant();
        user.IssueVerificationToken("h1", Now, Lifetime, Cooldown);
        user.ConfirmEmail(Now);

        user.IsRegistrationExpired(Now.AddDays(30)).ShouldBeFalse();
        NewUser().IsRegistrationExpired(Now).ShouldBeFalse();
    }
}
