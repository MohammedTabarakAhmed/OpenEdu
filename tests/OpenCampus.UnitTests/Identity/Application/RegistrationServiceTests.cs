using OpenCampus.Identity.Application.Authorization;
using OpenCampus.Identity.Application.Registration;
using OpenCampus.Identity.Domain.Audit;
using OpenCampus.Identity.Domain.Users;
using OpenCampus.SharedKernel;

namespace OpenCampus.UnitTests.Identity.Application;

/// <summary>Self-registration (Increment 7) at the application layer, with in-memory collaborators.</summary>
public class RegistrationServiceTests
{
    private const string Password = "Correct-Horse-Battery-Staple-1";

    [Fact]
    public async Task Register_CreatesInactiveRegistrantAndSendsVerificationLink()
    {
        var h = new RegistrationHarness();

        var result = await h.Registration.RegisterAsync(RegistrationHarness.Request("Instructor"), h.Client, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Message.ShouldBe(RegistrationAcceptedResponse.CheckInbox);

        var user = h.Users.Users.ShouldHaveSingleItem();
        user.IsActive.ShouldBeFalse();
        user.Roles.ShouldBeEmpty();
        user.RequestedRole.ShouldBe("Instructor");
        user.RegistrationStatus.ShouldBe(RegistrationStatus.AwaitingVerification);
        user.PasswordHash.ShouldStartWith(FakePasswordHasher.Prefix);
        user.VerificationTokenHash.ShouldBe("sha256:token-1"); // Only the hash is stored.
        user.VerificationTokenExpiresAtUtc.ShouldBe(RegistrationHarness.Start.AddHours(24));

        var mail = h.Email.To("newbie@example.org").ShouldHaveSingleItem();
        mail.Body.ShouldContain("https://localhost:4200/verify-email?token=token-1");
        mail.Body.ShouldContain("مرحباً");
        h.Audit.OfType(AuditEventTypes.UserRegistered).ShouldHaveSingleItem().DetailsJson.ShouldNotBeNull().ShouldContain("Instructor");
    }

    [Fact]
    public async Task Register_AcceptsAnyCasingOfAccountTypeAndStoresCanonicalName()
    {
        var h = new RegistrationHarness();

        await h.Registration.RegisterAsync(RegistrationHarness.Request("learner"), h.Client, CancellationToken.None);

        h.Users.Users.Single().RequestedRole.ShouldBe(RoleNames.Learner);
    }

    [Fact]
    public async Task VerifyEmail_Learner_ActivatesAssignsRoleProvisionsRecordAndWelcomes()
    {
        var h = new RegistrationHarness();
        await h.Registration.RegisterAsync(RegistrationHarness.Request("Learner"), h.Client, CancellationToken.None);
        var token = h.TokenSentTo("newbie@example.org");

        var result = await h.Registration.VerifyEmailAsync(new VerifyEmailRequest(token), h.Client, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Outcome.ShouldBe(VerifyEmailResponse.Activated);
        result.Value.RequestedRole.ShouldBe("Learner");

        var user = h.Users.Users.Single();
        user.IsActive.ShouldBeTrue();
        user.RegistrationStatus.ShouldBe(RegistrationStatus.Approved);
        user.Roles.Single().RoleId.ShouldBe(h.Roles["Learner"].Id);
        user.EmailVerifiedAtUtc.ShouldBe(RegistrationHarness.Start);
        user.VerificationTokenHash.ShouldBeNull();

        h.LearnerRecords.Provisioned.ShouldBe([user.Id]);
        h.Email.To("newbie@example.org").Count().ShouldBe(2);
        h.Email.Sent.Last().Subject.ShouldContain("ready");
        h.Audit.OfType(AuditEventTypes.EmailVerified).ShouldHaveSingleItem();
        h.Audit.OfType(AuditEventTypes.RoleAssigned).ShouldHaveSingleItem().UserId.ShouldBeNull(); // self-service: no actor
        h.Audit.OfType(AuditEventTypes.RegistrationApproved).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task VerifyEmail_Staff_StaysInactiveWithoutRoleAwaitingApproval()
    {
        var h = new RegistrationHarness();
        await h.Registration.RegisterAsync(RegistrationHarness.Request("Administrator"), h.Client, CancellationToken.None);

        var result = await h.Registration.VerifyEmailAsync(new VerifyEmailRequest(h.TokenSentTo("newbie@example.org")), h.Client, CancellationToken.None);

        result.Value.Outcome.ShouldBe(VerifyEmailResponse.AwaitingApproval);
        result.Value.RequestedRole.ShouldBe("Administrator");

        var user = h.Users.Users.Single();
        user.IsActive.ShouldBeFalse();
        user.Roles.ShouldBeEmpty();
        user.RegistrationStatus.ShouldBe(RegistrationStatus.AwaitingApproval);
        h.LearnerRecords.Provisioned.ShouldBeEmpty();
        h.Email.Sent.Last().Subject.ShouldContain("awaiting approval");
        h.Audit.OfType(AuditEventTypes.RoleAssigned).ShouldBeEmpty();
        h.Audit.OfType(AuditEventTypes.RegistrationApproved).ShouldBeEmpty();
    }

    [Fact]
    public async Task VerifyEmail_LearnerRecordFailure_IsLoggedNotSurfaced()
    {
        var h = new RegistrationHarness();
        h.LearnerRecords.Throws = new InvalidOperationException("SIS down");
        await h.Registration.RegisterAsync(RegistrationHarness.Request("Learner"), h.Client, CancellationToken.None);

        var result = await h.Registration.VerifyEmailAsync(new VerifyEmailRequest(h.TokenSentTo("newbie@example.org")), h.Client, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        h.Users.Users.Single().IsActive.ShouldBeTrue(); // Identity commit stands; recovery is the Learners screen.
    }

    [Fact]
    public async Task Register_DuplicateEmail_SameResponseNoUserAddedOwnerNotified()
    {
        var h = new RegistrationHarness();
        var owner = User.Create("owner@example.org", "owner", "hash", "Owner", "مالك");
        h.Users.Users.Add(owner);

        var result = await h.Registration.RegisterAsync(RegistrationHarness.Request("Learner", "intruder42", "Owner@Example.org"), h.Client, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(RegistrationAcceptedResponse.Default);
        h.Users.Users.ShouldHaveSingleItem().ShouldBe(owner);
        var mail = h.Email.To("owner@example.org").ShouldHaveSingleItem();
        mail.Subject.ShouldContain("tried to register");
        mail.Body.ShouldNotContain("intruder42"); // The attempted user name is not disclosed to the owner either.
        h.Audit.OfType(AuditEventTypes.RegistrationDuplicateEmail).ShouldHaveSingleItem().EntityId.ShouldBe(owner.Id);
        h.Audit.OfType(AuditEventTypes.UserRegistered).ShouldBeEmpty();
    }

    [Fact]
    public async Task Register_DuplicateUserName_Conflict()
    {
        var h = new RegistrationHarness();
        h.Users.Users.Add(User.Create("owner@example.org", "newbie", "hash", "Owner", "مالك"));

        var result = await h.Registration.RegisterAsync(RegistrationHarness.Request("Learner", "NEWBIE", "other@example.org"), h.Client, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(RegistrationErrors.UserNameTaken);
        result.Error.Type.ShouldBe(ErrorType.Conflict);
        h.Users.Users.Count.ShouldBe(1);
        h.Email.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task Register_StaleUnverifiedRow_IsReplaced()
    {
        var h = new RegistrationHarness();
        await h.Registration.RegisterAsync(RegistrationHarness.Request("Learner"), h.Client, CancellationToken.None);
        var stale = h.Users.Users.Single();
        h.Clock.Advance(TimeSpan.FromHours(25)); // past the 24 h token lifetime

        var result = await h.Registration.RegisterAsync(RegistrationHarness.Request("Instructor"), h.Client, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var fresh = h.Users.Users.ShouldHaveSingleItem();
        fresh.Id.ShouldNotBe(stale.Id);
        fresh.RequestedRole.ShouldBe("Instructor");
        h.Email.To("newbie@example.org").Count().ShouldBe(2);
    }

    [Fact]
    public async Task Register_LiveUnverifiedRow_IsNotReplaced()
    {
        var h = new RegistrationHarness();
        await h.Registration.RegisterAsync(RegistrationHarness.Request("Learner"), h.Client, CancellationToken.None);
        var pending = h.Users.Users.Single();

        // Same user name → conflict; same e-mail with a new user name → silent 202 + notice to the pending owner.
        (await h.Registration.RegisterAsync(RegistrationHarness.Request("Learner"), h.Client, CancellationToken.None)).Error.ShouldBe(RegistrationErrors.UserNameTaken);
        (await h.Registration.RegisterAsync(RegistrationHarness.Request("Learner", "another"), h.Client, CancellationToken.None)).IsSuccess.ShouldBeTrue();

        h.Users.Users.ShouldHaveSingleItem().ShouldBe(pending);
        h.Audit.OfType(AuditEventTypes.RegistrationDuplicateEmail).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task VerifyEmail_UnknownReusedOrExpiredToken_LinkInvalid()
    {
        var h = new RegistrationHarness();
        await h.Registration.RegisterAsync(RegistrationHarness.Request("Learner"), h.Client, CancellationToken.None);
        var token = h.TokenSentTo("newbie@example.org");

        (await h.Registration.VerifyEmailAsync(new VerifyEmailRequest("token-999"), h.Client, CancellationToken.None)).Error.ShouldBe(RegistrationErrors.LinkInvalid);

        (await h.Registration.VerifyEmailAsync(new VerifyEmailRequest(token), h.Client, CancellationToken.None)).IsSuccess.ShouldBeTrue();
        (await h.Registration.VerifyEmailAsync(new VerifyEmailRequest(token), h.Client, CancellationToken.None)).Error.ShouldBe(RegistrationErrors.LinkInvalid);

        var h2 = new RegistrationHarness();
        await h2.Registration.RegisterAsync(RegistrationHarness.Request("Learner"), h2.Client, CancellationToken.None);
        var token2 = h2.TokenSentTo("newbie@example.org");
        h2.Clock.Advance(TimeSpan.FromHours(24));
        var expired = await h2.Registration.VerifyEmailAsync(new VerifyEmailRequest(token2), h2.Client, CancellationToken.None);
        expired.Error.ShouldBe(RegistrationErrors.LinkInvalid);
        expired.Error.Type.ShouldBe(ErrorType.NotFound);
        h2.Users.Users.Single().RegistrationStatus.ShouldBe(RegistrationStatus.AwaitingVerification);
    }

    [Fact]
    public async Task Resend_ReplacesTokenAfterCooldown_SilentInsideCooldownOrForUnknownAddress()
    {
        var h = new RegistrationHarness();
        await h.Registration.RegisterAsync(RegistrationHarness.Request("Learner"), h.Client, CancellationToken.None);
        var first = h.TokenSentTo("newbie@example.org");

        // Inside the 60 s cooldown: accepted, nothing sent.
        h.Clock.Advance(TimeSpan.FromSeconds(30));
        (await h.Registration.ResendVerificationAsync(new ResendVerificationRequest("newbie@example.org"), h.Client, CancellationToken.None)).IsSuccess.ShouldBeTrue();
        h.Email.Sent.Count.ShouldBe(1);

        // After the cooldown: a new token; the old one no longer works.
        h.Clock.Advance(TimeSpan.FromSeconds(31));
        (await h.Registration.ResendVerificationAsync(new ResendVerificationRequest("newbie@example.org"), h.Client, CancellationToken.None)).IsSuccess.ShouldBeTrue();
        h.Email.Sent.Count.ShouldBe(2);
        var second = h.TokenSentTo("newbie@example.org");
        second.ShouldNotBe(first);
        (await h.Registration.VerifyEmailAsync(new VerifyEmailRequest(first), h.Client, CancellationToken.None)).Error.ShouldBe(RegistrationErrors.LinkInvalid);
        (await h.Registration.VerifyEmailAsync(new VerifyEmailRequest(second), h.Client, CancellationToken.None)).IsSuccess.ShouldBeTrue();

        // Unknown address and an already-verified account: the same success, nothing sent.
        var before = h.Email.Sent.Count;
        (await h.Registration.ResendVerificationAsync(new ResendVerificationRequest("nobody@example.org"), h.Client, CancellationToken.None)).IsSuccess.ShouldBeTrue();
        (await h.Registration.ResendVerificationAsync(new ResendVerificationRequest("newbie@example.org"), h.Client, CancellationToken.None)).IsSuccess.ShouldBeTrue();
        h.Email.Sent.Count.ShouldBe(before);
    }

    [Fact]
    public async Task Disabled_AllThreeOperationsAnswerNotFoundAndChangeNothing()
    {
        var h = new RegistrationHarness();
        h.Options.Enabled = false;

        (await h.Registration.RegisterAsync(RegistrationHarness.Request(), h.Client, CancellationToken.None)).Error.ShouldBe(RegistrationErrors.Disabled);
        (await h.Registration.VerifyEmailAsync(new VerifyEmailRequest("token-1"), h.Client, CancellationToken.None)).Error.ShouldBe(RegistrationErrors.Disabled);
        (await h.Registration.ResendVerificationAsync(new ResendVerificationRequest("a@b.org"), h.Client, CancellationToken.None)).Error.ShouldBe(RegistrationErrors.Disabled);

        RegistrationErrors.Disabled.Type.ShouldBe(ErrorType.NotFound);
        h.Users.Users.ShouldBeEmpty();
        h.Email.Sent.ShouldBeEmpty();
        h.Audit.Events.ShouldBeEmpty();
    }

    // SEC-02: the password never appears in a message, an audit detail or a stored field other than the hash.
    [Fact]
    public async Task Sec02_PasswordNeverLeavesTheHasher()
    {
        var h = new RegistrationHarness();
        await h.Registration.RegisterAsync(RegistrationHarness.Request("Learner"), h.Client, CancellationToken.None);
        await h.Registration.VerifyEmailAsync(new VerifyEmailRequest(h.TokenSentTo("newbie@example.org")), h.Client, CancellationToken.None);

        foreach (var mail in h.Email.Sent)
        {
            mail.Body.ShouldNotContain(Password);
            mail.Body.ShouldNotContain(FakePasswordHasher.Prefix);
            mail.Subject.ShouldNotContain(Password);
        }

        foreach (var audit in h.Audit.Events)
        {
            (audit.DetailsJson ?? string.Empty).ShouldNotContain(Password);
            (audit.DetailsJson ?? string.Empty).ShouldNotContain("token");
            (audit.DetailsJson ?? string.Empty).ShouldNotContain("sha256");
        }
    }

    // Administrator side (UserAdministrationService).

    [Fact]
    public async Task Approve_AssignsRequestedRoleActivatesAuditsAndNotifies()
    {
        var h = new RegistrationHarness();
        await h.Registration.RegisterAsync(RegistrationHarness.Request("Registrar"), h.Client, CancellationToken.None);
        await h.Registration.VerifyEmailAsync(new VerifyEmailRequest(h.TokenSentTo("newbie@example.org")), h.Client, CancellationToken.None);
        var user = h.Users.Users.Single();

        var result = await h.Administration.ApproveRegistrationAsync(user.Id, h.Client, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.IsActive.ShouldBeTrue();
        result.Value.Roles.ShouldBe(["Registrar"]);
        result.Value.RegistrationStatus.ShouldBe(RegistrationStatus.Approved);
        h.Audit.OfType(AuditEventTypes.RoleAssigned).ShouldHaveSingleItem().UserId.ShouldBe(h.CurrentUser.UserId);
        h.Audit.OfType(AuditEventTypes.RegistrationApproved).ShouldHaveSingleItem().UserId.ShouldBe(h.CurrentUser.UserId);
        h.Email.Sent.Last().Subject.ShouldContain("approved");
        h.LearnerRecords.Provisioned.ShouldBeEmpty();

        // Approving twice is refused.
        (await h.Administration.ApproveRegistrationAsync(user.Id, h.Client, CancellationToken.None)).Error.ShouldBe(RegistrationErrors.NotPending);
    }

    [Fact]
    public async Task Approve_RefusedBeforeVerificationAndForAdminCreatedUsers()
    {
        var h = new RegistrationHarness();
        await h.Registration.RegisterAsync(RegistrationHarness.Request("Instructor"), h.Client, CancellationToken.None);
        var pending = h.Users.Users.Single();
        var ordinary = User.Create("o@example.org", "ordinary", "hash", "O", "و");
        h.Users.Users.Add(ordinary);

        (await h.Administration.ApproveRegistrationAsync(pending.Id, h.Client, CancellationToken.None)).Error.ShouldBe(RegistrationErrors.NotVerified);
        (await h.Administration.ApproveRegistrationAsync(ordinary.Id, h.Client, CancellationToken.None)).Error.ShouldBe(RegistrationErrors.NotPending);
        (await h.Administration.ApproveRegistrationAsync(Guid.NewGuid(), h.Client, CancellationToken.None)).Error.Type.ShouldBe(ErrorType.NotFound);

        pending.Roles.ShouldBeEmpty();
        pending.IsActive.ShouldBeFalse();
    }

    [Fact]
    public async Task Approve_LearnerByAdministrator_ProvisionsLearnerRecord()
    {
        var h = new RegistrationHarness();
        var user = User.Register("l@example.org", "learner1", "hash", "L", "ل", "Learner");
        user.IssueVerificationToken("h", RegistrationHarness.Start, TimeSpan.FromHours(1), TimeSpan.Zero);
        user.ConfirmEmail(RegistrationHarness.Start);
        h.Users.Users.Add(user);

        var result = await h.Administration.ApproveRegistrationAsync(user.Id, h.Client, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        h.LearnerRecords.Provisioned.ShouldBe([user.Id]);
    }

    [Fact]
    public async Task Reject_RemovesRowAuditsAndNotifiesOnlyVerifiedAddresses()
    {
        var h = new RegistrationHarness();
        await h.Registration.RegisterAsync(RegistrationHarness.Request("Instructor"), h.Client, CancellationToken.None);
        await h.Registration.VerifyEmailAsync(new VerifyEmailRequest(h.TokenSentTo("newbie@example.org")), h.Client, CancellationToken.None);
        var verified = h.Users.Users.Single();
        await h.Registration.RegisterAsync(RegistrationHarness.Request("Administrator", "unverified", "u@example.org"), h.Client, CancellationToken.None);
        var unverified = h.Users.Users.Single(u => u.UserName == "unverified");

        (await h.Administration.RejectRegistrationAsync(verified.Id, h.Client, CancellationToken.None)).IsSuccess.ShouldBeTrue();
        (await h.Administration.RejectRegistrationAsync(unverified.Id, h.Client, CancellationToken.None)).IsSuccess.ShouldBeTrue();

        h.Users.Users.ShouldBeEmpty();
        h.Audit.OfType(AuditEventTypes.RegistrationRejected).Count().ShouldBe(2);
        h.Email.To("newbie@example.org").Last().Subject.ShouldContain("not approved");
        h.Email.To("u@example.org").Count().ShouldBe(1); // only the original verification message, no rejection notice

        // The freed identifiers can be registered again.
        (await h.Registration.RegisterAsync(RegistrationHarness.Request("Learner"), h.Client, CancellationToken.None)).IsSuccess.ShouldBeTrue();
        h.Users.Users.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Reject_RefusedForActiveAccounts()
    {
        var h = new RegistrationHarness();
        var ordinary = User.Create("o@example.org", "ordinary", "hash", "O", "و");
        h.Users.Users.Add(ordinary);

        (await h.Administration.RejectRegistrationAsync(ordinary.Id, h.Client, CancellationToken.None)).Error.ShouldBe(RegistrationErrors.NotPending);
        h.Users.Users.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Activate_OnPendingRegistrant_IsRuleViolation()
    {
        var h = new RegistrationHarness();
        await h.Registration.RegisterAsync(RegistrationHarness.Request("Administrator"), h.Client, CancellationToken.None);
        var pending = h.Users.Users.Single();

        var result = await h.Administration.ActivateAsync(pending.Id, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Type.ShouldBe(ErrorType.RuleViolation);
        result.Error.Code.ShouldBe("registration.not_pending");
        pending.IsActive.ShouldBeFalse();
    }

    [Fact]
    public async Task List_FiltersByRegistrationStatusAndExposesRequestedRole()
    {
        var h = new RegistrationHarness();
        await h.Registration.RegisterAsync(RegistrationHarness.Request("Instructor"), h.Client, CancellationToken.None);
        await h.Registration.VerifyEmailAsync(new VerifyEmailRequest(h.TokenSentTo("newbie@example.org")), h.Client, CancellationToken.None);
        h.Users.Users.Add(User.Create("o@example.org", "ordinary", "hash", "O", "و"));

        var page = await h.Administration.ListAsync(new OpenCampus.Identity.Application.Administration.UserListQuery(null, null, null, null, null, RegistrationStatus.AwaitingApproval), CancellationToken.None);

        var item = page.Items.ShouldHaveSingleItem();
        item.UserName.ShouldBe("newbie");
        item.RegistrationStatus.ShouldBe(RegistrationStatus.AwaitingApproval);
        item.RequestedRole.ShouldBe("Instructor");
        item.EmailVerifiedAtUtc.ShouldBe(RegistrationHarness.Start);
    }
}
