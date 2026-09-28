using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenCampus.Identity.Application.Administration;
using OpenCampus.Identity.Application.Authorization;
using OpenCampus.Identity.Application.Registration;
using OpenCampus.Identity.Domain.Audit;
using OpenCampus.Identity.Domain.Users;
using OpenCampus.Identity.Infrastructure.Persistence;
using OpenCampus.SharedKernel;
using OpenCampus.Sis.Infrastructure.Persistence;
using static OpenCampus.IntegrationTests.Identity.AuthTestSupport;

namespace OpenCampus.IntegrationTests.Identity;

/// <summary>
/// Increment 7 — self-registration through the real host: learner self-service, staff approval, enumeration safety,
/// token hygiene, abuse controls and SEC-02, as listed in the plan's cases 1–12.
/// </summary>
[Collection(ApiCollection.Name)]
public partial class RegistrationTests(ApiFactory factory)
{
    private const string Password = "Correct-Horse-Battery-Staple-1";

    [GeneratedRegex(@"verify-email\?token=([A-Za-z0-9_\-%]+)")]
    private static partial Regex TokenLink();

    // ----- helpers -----

    private static RegisterRequest NewRequest(string accountType, string? suffix = null)
    {
        suffix ??= Guid.NewGuid().ToString("N")[..10];
        return new RegisterRequest(accountType, $"reg_{suffix}", $"reg_{suffix}@example.org", Password, $"Reg {suffix}", "تسجيل");
    }

    private IEnumerable<JsonElement> Emails() =>
        File.Exists(Path.Combine(factory.NotificationRoot, "email.jsonl"))
            ? File.ReadLines(Path.Combine(factory.NotificationRoot, "email.jsonl")).Where(l => l.Length > 0).Select(l => JsonSerializer.Deserialize<JsonElement>(l))
            : [];

    private List<JsonElement> EmailsTo(string address) =>
        Emails().Where(e => e.GetProperty("to").EnumerateArray().Any(t => string.Equals(t.GetString(), address, StringComparison.OrdinalIgnoreCase))).ToList();

    private string LatestTokenFor(string address)
    {
        var body = EmailsTo(address).Last(e => e.GetProperty("body").GetString()!.Contains("verify-email?token=")).GetProperty("body").GetString()!;
        return Uri.UnescapeDataString(TokenLink().Match(body).Groups[1].Value);
    }

    private async Task<(User User, string Token)> RegisterAsync(HttpClient client, RegisterRequest request)
    {
        var response = await client.PostAsJsonAsync("/api/v1/registration", request);
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadFromJsonAsync<RegistrationAcceptedResponse>(Json))!.Message.ShouldBe(RegistrationAcceptedResponse.CheckInbox);

        using var scope = factory.Services.CreateScope();
        var user = await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Users.AsNoTracking().SingleAsync(u => u.UserName == request.UserName);
        return (user, LatestTokenFor(request.Email));
    }

    private static async Task<VerifyEmailResponse> VerifyAsync(HttpClient client, string token, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var response = await client.PostAsJsonAsync("/api/v1/registration/verify-email", new VerifyEmailRequest(token));
        response.StatusCode.ShouldBe(expected, await response.Content.ReadAsStringAsync());
        return expected == HttpStatusCode.OK ? (await response.Content.ReadFromJsonAsync<VerifyEmailResponse>(Json))! : null!;
    }

    private async Task<HttpClient> AdminClientAsync()
    {
        var client = factory.CreateApiClient();
        var (_, token, _) = await LoginAsRoleAsync(factory, client, RoleNames.Administrator);
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<HttpClient> RoleClientAsync(string role)
    {
        var client = factory.CreateApiClient();
        var (_, token, _) = await LoginAsRoleAsync(factory, client, role);
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<List<AuditEvent>> AuditForEntityAsync(Guid entityId, string eventType)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().AuditEvents
            .Where(a => a.EntityId == entityId && a.EventType == eventType).OrderBy(a => a.OccurredAtUtc).ToListAsync();
    }

    private async Task ExpireTokenAsync(Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Users
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.VerificationTokenExpiresAtUtc, DateTime.UtcNow.AddMinutes(-1)));
    }

    // ----- 1. Learner happy path -----

    [Fact]
    public async Task Case01_Learner_RegisterVerifySignInAndEnrolmentSelfServiceWorks()
    {
        using var client = factory.CreateApiClient();
        var request = NewRequest(RoleNames.Learner);

        var (pending, token) = await RegisterAsync(client, request);
        pending.IsActive.ShouldBeFalse();
        pending.RegistrationStatus.ShouldBe(RegistrationStatus.AwaitingVerification);
        pending.RequestedRole.ShouldBe("Learner");
        pending.VerificationTokenHash.ShouldNotBeNull();
        pending.VerificationTokenHash.ShouldNotBe(token);           // only the hash is stored
        pending.VerificationTokenHash.ShouldNotContain(token);
        token.Length.ShouldBeGreaterThanOrEqualTo(43);               // 32 bytes base64url

        var mail = EmailsTo(request.Email).ShouldHaveSingleItem();
        mail.GetProperty("origin").GetString()!.ShouldBe("identity");
        mail.GetProperty("subject").GetString()!.ShouldContain("Verify");
        mail.GetProperty("body").GetString()!.ShouldContain("مرحباً");

        // Cannot sign in yet — indistinguishable 401 (SEC-15).
        (await LoginAsync(client, request.UserName, Password)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var verified = await VerifyAsync(client, token);
        verified.Outcome.ShouldBe(VerifyEmailResponse.Activated);
        verified.RequestedRole.ShouldBe("Learner");

        var (login, _) = await LoginExpectingSessionAsync(client, request.UserName, Password);
        login.Authenticated!.Principal.Roles.ShouldBe(["Learner"]);
        var bearer = login.Authenticated.AccessToken;

        // The SIS learner record exists with a generated number, so learner self-service answers (not 404 learners.none_for_caller).
        var enrolments = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/api/v1/me/enrolments").WithBearer(bearer));
        enrolments.StatusCode.ShouldBe(HttpStatusCode.OK, await enrolments.Content.ReadAsStringAsync());

        using (var scope = factory.Services.CreateScope())
        {
            var learner = await scope.ServiceProvider.GetRequiredService<SisDbContext>().Learners.AsNoTracking().SingleAsync(l => l.UserId == pending.Id);
            learner.LearnerNumber.ShouldMatch(@"^L\d{8}$");
            learner.LearnerNumber.ShouldStartWith($"L{DateTime.UtcNow.Year}1");
        }

        var activated = await ReloadUserAsync(factory, pending.Id);
        activated.IsActive.ShouldBeTrue();
        activated.RegistrationStatus.ShouldBe(RegistrationStatus.Approved);
        activated.EmailVerifiedAtUtc.ShouldNotBeNull();
        activated.VerificationTokenHash.ShouldBeNull();

        (await AuditForEntityAsync(pending.Id, AuditEventTypes.UserRegistered)).ShouldHaveSingleItem().DetailsJson.ShouldNotBeNull().ShouldContain("Learner");
        (await AuditForEntityAsync(pending.Id, AuditEventTypes.EmailVerified)).ShouldHaveSingleItem();
        (await AuditForEntityAsync(pending.Id, AuditEventTypes.RoleAssigned)).ShouldHaveSingleItem().UserId.ShouldBeNull();
        (await AuditForEntityAsync(pending.Id, AuditEventTypes.RegistrationApproved)).ShouldHaveSingleItem();
        EmailsTo(request.Email).Count.ShouldBe(2);
        EmailsTo(request.Email).Last().GetProperty("subject").GetString()!.ShouldContain("ready");
    }

    // ----- 2. Staff path: Instructor -----

    [Fact]
    public async Task Case02_Instructor_AwaitsApproval_RegistrarCannotApprove_AdministratorCan()
    {
        using var client = factory.CreateApiClient();
        var request = NewRequest(RoleNames.Instructor);
        var (pending, token) = await RegisterAsync(client, request);

        (await VerifyAsync(client, token)).Outcome.ShouldBe(VerifyEmailResponse.AwaitingApproval);
        (await LoginAsync(client, request.UserName, Password)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        EmailsTo(request.Email).Last().GetProperty("subject").GetString()!.ShouldContain("awaiting approval");

        using var admin = await AdminClientAsync();
        var list = await (await admin.GetAsync("/api/v1/users?registrationStatus=AwaitingApproval&pageSize=100")).Content.ReadFromJsonAsync<PagedResponse<UserResponse>>(Json);
        list!.Items.ShouldContain(u => u.Id == pending.Id);
        var listed = list.Items.Single(u => u.Id == pending.Id);
        listed.RequestedRole.ShouldBe("Instructor");
        listed.RegistrationStatus.ShouldBe(RegistrationStatus.AwaitingApproval);
        listed.Roles.ShouldBeEmpty();
        listed.IsActive.ShouldBeFalse();

        using var registrar = await RoleClientAsync(RoleNames.Registrar);
        (await registrar.PostAsync($"/api/v1/users/{pending.Id}/registration/approve", null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await registrar.PostAsync($"/api/v1/users/{pending.Id}/registration/reject", null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await LoginAsync(client, request.UserName, Password)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var approve = await admin.PostAsync($"/api/v1/users/{pending.Id}/registration/approve", null);
        approve.StatusCode.ShouldBe(HttpStatusCode.OK, await approve.Content.ReadAsStringAsync());
        var approved = (await approve.Content.ReadFromJsonAsync<UserResponse>(Json))!;
        approved.Roles.ShouldBe(["Instructor"]);
        approved.IsActive.ShouldBeTrue();
        approved.RegistrationStatus.ShouldBe(RegistrationStatus.Approved);
        EmailsTo(request.Email).Last().GetProperty("subject").GetString()!.ShouldContain("approved");

        var (login, _) = await LoginExpectingSessionAsync(client, request.UserName, Password);
        login.Authenticated!.Principal.Roles.ShouldBe(["Instructor"]);

        (await AuditForEntityAsync(pending.Id, AuditEventTypes.RegistrationApproved)).ShouldHaveSingleItem().UserId.ShouldNotBeNull();
        (await admin.PostAsync($"/api/v1/users/{pending.Id}/registration/approve", null)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    // ----- 3. Administrator path: no self-grant -----

    [Fact]
    public async Task Case03_Administrator_GainsRoleOnlyThroughExistingAdministrator()
    {
        using var client = factory.CreateApiClient();
        var request = NewRequest(RoleNames.Administrator);
        var (pending, token) = await RegisterAsync(client, request);
        (await VerifyAsync(client, token)).Outcome.ShouldBe(VerifyEmailResponse.AwaitingApproval);

        (await ReloadUserAsync(factory, pending.Id)).IsActive.ShouldBeFalse();
        (await LoginAsync(client, request.UserName, Password)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        using (var scope = factory.Services.CreateScope())
        {
            (await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Set<UserRole>().CountAsync(r => r.UserId == pending.Id)).ShouldBe(0);
        }

        // Approval before verification is refused (a second, unverified administrator request).
        var (unverified, _) = await RegisterAsync(client, NewRequest(RoleNames.Administrator));
        using var admin = await AdminClientAsync();
        var early = await admin.PostAsync($"/api/v1/users/{unverified.Id}/registration/approve", null);
        early.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await early.Content.ReadFromJsonAsync<ProblemDetails>(Json))!.Title.ShouldBe("registration.not_verified");

        var approve = await admin.PostAsync($"/api/v1/users/{pending.Id}/registration/approve", null);
        approve.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await approve.Content.ReadFromJsonAsync<UserResponse>(Json))!.Roles.ShouldBe(["Administrator"]);
        var (login, _) = await LoginExpectingSessionAsync(client, request.UserName, Password);
        login.Authenticated!.Principal.Roles.ShouldBe(["Administrator"]);
    }

    // ----- 4. Reject -----

    [Fact]
    public async Task Case04_Reject_RemovesRowAuditsAndFreesTheAddress()
    {
        using var client = factory.CreateApiClient();
        var request = NewRequest(RoleNames.Registrar);
        var (pending, token) = await RegisterAsync(client, request);
        await VerifyAsync(client, token);

        using var admin = await AdminClientAsync();
        (await admin.PostAsync($"/api/v1/users/{pending.Id}/registration/reject", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await admin.GetAsync($"/api/v1/users/{pending.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await AuditForEntityAsync(pending.Id, AuditEventTypes.RegistrationRejected)).ShouldHaveSingleItem().DetailsJson.ShouldNotBeNull().ShouldContain("Registrar");
        EmailsTo(request.Email).Last().GetProperty("subject").GetString()!.ShouldContain("not approved");

        // The same e-mail and user name can be registered again.
        var (again, _) = await RegisterAsync(client, request);
        again.Id.ShouldNotBe(pending.Id);

        // Rejecting an ordinary (admin-created) account is refused.
        var ordinary = await SeedUserAsync(factory, roles: RoleNames.Learner);
        (await admin.PostAsync($"/api/v1/users/{ordinary.Id}/registration/reject", null)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await admin.GetAsync($"/api/v1/users/{ordinary.Id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // ----- 5. Duplicate e-mail: enumeration-safe -----

    [Fact]
    public async Task Case05_DuplicateEmail_SameAcceptedBody_NoUserCreated_OwnerNotified()
    {
        using var client = factory.CreateApiClient();
        var owner = await SeedUserAsync(factory, roles: RoleNames.Learner);
        var fresh = NewRequest(RoleNames.Learner);
        var duplicate = NewRequest(RoleNames.Learner) with { Email = owner.Email.ToUpperInvariant() };

        var first = await client.PostAsJsonAsync("/api/v1/registration", fresh);
        var second = await client.PostAsJsonAsync("/api/v1/registration", duplicate);

        first.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        second.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await second.Content.ReadAsStringAsync()).ShouldBe(await first.Content.ReadAsStringAsync());

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            (await db.Users.AnyAsync(u => u.UserName == duplicate.UserName)).ShouldBeFalse();
            (await db.Users.CountAsync(u => u.Email == owner.Email)).ShouldBe(1);
        }

        var notice = EmailsTo(owner.Email).ShouldHaveSingleItem();
        notice.GetProperty("subject").GetString()!.ShouldContain("tried to register");
        notice.GetProperty("body").GetString()!.ShouldNotContain(duplicate.UserName);
        (await AuditForEntityAsync(owner.Id, AuditEventTypes.RegistrationDuplicateEmail)).ShouldHaveSingleItem();
    }

    // ----- 6. Duplicate user name / stale row replacement -----

    [Fact]
    public async Task Case06_DuplicateUserName_Conflict_StaleExpiredRowReplaced()
    {
        using var client = factory.CreateApiClient();
        var owner = await SeedUserAsync(factory, roles: RoleNames.Learner);

        var clash = await client.PostAsJsonAsync("/api/v1/registration", NewRequest(RoleNames.Learner) with { UserName = owner.UserName.ToUpperInvariant() });
        clash.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await clash.Content.ReadFromJsonAsync<ProblemDetails>(Json))!.Title.ShouldBe("users.user_name_taken");

        // A live pending registration also holds its user name...
        var request = NewRequest(RoleNames.Learner);
        var (stale, _) = await RegisterAsync(client, request);
        (await client.PostAsJsonAsync("/api/v1/registration", request with { Email = "other-" + request.Email })).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        // ...until its token expires, when a new registration replaces it.
        await ExpireTokenAsync(stale.Id);
        var (replacement, _) = await RegisterAsync(client, request with { AccountType = RoleNames.Instructor });
        replacement.Id.ShouldNotBe(stale.Id);
        replacement.RequestedRole.ShouldBe("Instructor");
        using var scope = factory.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Users.AnyAsync(u => u.Id == stale.Id)).ShouldBeFalse();
    }

    // ----- 7. Token hygiene -----

    [Fact]
    public async Task Case07_Token_SingleUse_Expires_ResendReplaces_CooldownAndUnknownAreSilent()
    {
        using var client = factory.CreateApiClient();

        // Reuse.
        var request = NewRequest(RoleNames.Learner);
        var (_, token) = await RegisterAsync(client, request);
        await VerifyAsync(client, token);
        var reused = await client.PostAsJsonAsync("/api/v1/registration/verify-email", new VerifyEmailRequest(token));
        reused.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await reused.Content.ReadFromJsonAsync<ProblemDetails>(Json))!.Title.ShouldBe("registration.link_invalid");

        // Expiry.
        var expiring = NewRequest(RoleNames.Learner);
        var (user, expiringToken) = await RegisterAsync(client, expiring);
        await ExpireTokenAsync(user.Id);
        await VerifyAsync(client, expiringToken, HttpStatusCode.NotFound);
        (await ReloadUserAsync(factory, user.Id)).RegistrationStatus.ShouldBe(RegistrationStatus.AwaitingVerification);

        // Resend inside the cooldown: 202, nothing sent.
        var before = EmailsTo(expiring.Email).Count;
        (await client.PostAsJsonAsync("/api/v1/registration/resend-verification", new ResendVerificationRequest(expiring.Email))).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        EmailsTo(expiring.Email).Count.ShouldBe(before);

        // Resend after the cooldown replaces the token (cooldown elapsed by rewinding the issue time).
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Users.Where(u => u.Id == user.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.VerificationTokenIssuedAtUtc, DateTime.UtcNow.AddMinutes(-5)));
        }

        (await client.PostAsJsonAsync("/api/v1/registration/resend-verification", new ResendVerificationRequest(expiring.Email))).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        EmailsTo(expiring.Email).Count.ShouldBe(before + 1);
        var replacement = LatestTokenFor(expiring.Email);
        replacement.ShouldNotBe(expiringToken);
        await VerifyAsync(client, expiringToken, HttpStatusCode.NotFound);
        (await VerifyAsync(client, replacement)).Outcome.ShouldBe(VerifyEmailResponse.Activated);

        // Unknown address: the same 202 and no message.
        var unknown = $"nobody-{Guid.NewGuid():N}@example.org";
        (await client.PostAsJsonAsync("/api/v1/registration/resend-verification", new ResendVerificationRequest(unknown))).StatusCode.ShouldBe(HttpStatusCode.Accepted);
        EmailsTo(unknown).ShouldBeEmpty();

        // Column inspection: no clear token anywhere in the Users table.
        using (var scope = factory.Services.CreateScope())
        {
            var hashes = await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Users
                .Where(u => u.VerificationTokenHash != null).Select(u => u.VerificationTokenHash!).ToListAsync();
            hashes.ShouldAllBe(h => h.Length == 64 && h != token && h != replacement);
        }
    }

    // ----- 8. Validation -----

    [Fact]
    public async Task Case08_Validation_ReturnsFieldKeyed400()
    {
        using var client = factory.CreateApiClient();
        var valid = NewRequest(RoleNames.Learner);

        async Task<ValidationProblemDetails> Invalid(RegisterRequest request)
        {
            var response = await client.PostAsJsonAsync("/api/v1/registration", request);
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            return (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(Json))!;
        }

        (await Invalid(valid with { AccountType = "Owner" })).Errors.Keys.ShouldBe(["AccountType"]);
        (await Invalid(valid with { Password = "Eleven-char" })).Errors.Keys.ShouldBe(["Password"]);
        (await Invalid(valid with { Password = "qwerty123456" })).Errors["Password"].ShouldContain(m => m.Contains("common"));
        (await Invalid(valid with { Password = $"xx{valid.UserName}xx" })).Errors["Password"].ShouldContain(m => m.Contains("user name"));
        (await Invalid(valid with { Email = "not-an-email" })).Errors.Keys.ShouldBe(["Email"]);
        (await Invalid(valid with { UserName = "has space" })).Errors.Keys.ShouldBe(["UserName"]);

        var badToken = await client.PostAsJsonAsync("/api/v1/registration/verify-email", new VerifyEmailRequest("bad/token"));
        badToken.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    // ----- 9. Rate limit -----

    [Fact]
    public async Task Case09_RegistrationRoutes_AreRateLimited()
    {
        const int limit = 3;
        using var limited = factory.WithWebHostBuilder(builder => builder.UseSetting("AccountProtection:RateLimitPermittedRequests", limit.ToString()));
        using var client = limited.CreateClient();
        var invalid = NewRequest(RoleNames.Learner) with { Email = "not-an-email" };

        for (var i = 0; i < limit; i++)
        {
            (await client.PostAsJsonAsync("/api/v1/registration", invalid)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }

        var rejected = await client.PostAsJsonAsync("/api/v1/registration", invalid);
        rejected.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        rejected.Headers.RetryAfter.ShouldNotBeNull();
    }

    // ----- 10. Activate cannot bypass approval -----

    [Fact]
    public async Task Case10_ActivateOnPendingRegistrant_Is422()
    {
        using var client = factory.CreateApiClient();
        var (pending, token) = await RegisterAsync(client, NewRequest(RoleNames.Administrator));
        using var admin = await AdminClientAsync();

        var beforeVerification = await admin.PostAsync($"/api/v1/users/{pending.Id}/activate", null);
        beforeVerification.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        await VerifyAsync(client, token);
        var afterVerification = await admin.PostAsync($"/api/v1/users/{pending.Id}/activate", null);
        afterVerification.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await afterVerification.Content.ReadFromJsonAsync<ProblemDetails>(Json))!.Title.ShouldBe("registration.not_pending");

        var user = await ReloadUserAsync(factory, pending.Id);
        user.IsActive.ShouldBeFalse();
        (await LoginAsync(client, user.UserName, Password)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // ----- 11. SEC-02 and the feature switch -----

    [Fact]
    public async Task Case11_NoSecretInResponsesOrMail_AndDisabledHostAnswers404()
    {
        using var client = factory.CreateApiClient();
        var request = NewRequest(RoleNames.Learner);
        var response = await client.PostAsJsonAsync("/api/v1/registration", request);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldNotContain(Password);
        body.ShouldNotContain("hash", Case.Insensitive);
        body.ShouldNotContain("token", Case.Insensitive);

        User stored;
        using (var scope = factory.Services.CreateScope())
        {
            stored = await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Users.AsNoTracking().SingleAsync(u => u.UserName == request.UserName);
        }

        stored.PasswordHash.ShouldStartWith("PBKDF2-SHA256");
        stored.PasswordHash.ShouldNotContain(Password);

        foreach (var mail in EmailsTo(request.Email))
        {
            mail.GetProperty("body").GetString()!.ShouldNotContain(Password);
            mail.GetProperty("body").GetString()!.ShouldNotContain(stored.PasswordHash);
            mail.GetProperty("body").GetString()!.ShouldNotContain(stored.VerificationTokenHash!);
        }

        using var admin = await AdminClientAsync();
        var listed = await (await admin.GetAsync($"/api/v1/users?search={request.UserName}")).Content.ReadAsStringAsync();
        listed.ShouldNotContain(stored.PasswordHash);
        listed.ShouldNotContain(stored.VerificationTokenHash!);

        using var disabled = factory.WithWebHostBuilder(builder => builder.UseSetting("Registration:Enabled", "false"));
        using var off = disabled.CreateClient();
        (await off.PostAsJsonAsync("/api/v1/registration", NewRequest(RoleNames.Learner))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await off.PostAsJsonAsync("/api/v1/registration/verify-email", new VerifyEmailRequest(LatestTokenFor(request.Email)))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await off.PostAsJsonAsync("/api/v1/registration/resend-verification", new ResendVerificationRequest(request.Email))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // ----- 12. Anonymous surface -----

    [Fact]
    public void Case12_AnonymousEndpoints_AreExactlyTheDocumentedSet()
    {
        var actions = factory.Services.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors.Items;

        var anonymous = actions
            .Where(a => a.EndpointMetadata.OfType<IAllowAnonymous>().Any())
            .Select(a => $"{a.ActionConstraints?.OfType<Microsoft.AspNetCore.Mvc.ActionConstraints.HttpMethodActionConstraint>().FirstOrDefault()?.HttpMethods.First()} /{a.AttributeRouteInfo!.Template}")
            .OrderBy(s => s)
            .ToList();

        anonymous.ShouldBe(
        [
            "GET /api/v1/certificates/verify/{code}",
            "POST /api/v1/auth/login",
            "POST /api/v1/auth/mfa/verify",
            "POST /api/v1/auth/refresh",
            "POST /api/v1/registration",
            "POST /api/v1/registration/resend-verification",
            "POST /api/v1/registration/verify-email",
        ]);
    }
}
