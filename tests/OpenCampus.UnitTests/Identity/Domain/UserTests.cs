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
}
