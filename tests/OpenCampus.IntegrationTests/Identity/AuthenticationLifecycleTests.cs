using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using OpenCampus.Api.Controllers;
using OpenCampus.Api.Security;
using OpenCampus.Identity.Application.Authentication;
using OpenCampus.Identity.Domain.Audit;
using static OpenCampus.IntegrationTests.Identity.AuthTestSupport;

namespace OpenCampus.IntegrationTests.Identity;

/// <summary>End-to-end authentication lifecycle against the database (SDD 20.1, integration level).</summary>
[Collection(ApiCollection.Name)]
public class AuthenticationLifecycleTests(ApiFactory factory)
{
    // SEC-03, SEC-05: a session yields a short-lived bearer token and a hardened refresh cookie.
    [Fact]
    public async Task Login_WithValidCredentials_IssuesAccessTokenAndRefreshCookie()
    {
        var user = await SeedUserAsync(factory);
        using var client = factory.CreateApiClient();

        var response = await LoginAsync(client, user.UserName, DefaultPassword);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = (await response.Content.ReadFromJsonAsync<LoginResponse>(Json))!;
        body.MfaRequired.ShouldBeFalse();
        body.Authenticated!.AccessToken.ShouldNotBeNullOrEmpty();
        (body.Authenticated.AccessTokenExpiresAtUtc - DateTime.UtcNow).ShouldBeLessThanOrEqualTo(TimeSpan.FromMinutes(15));
        body.Authenticated.Principal.Id.ShouldBe(user.Id);
        body.Authenticated.Principal.UserName.ShouldBe(user.UserName);

        var cookie = ReadRefreshCookieHeader(response)!;
        cookie.ShouldContain("httponly", Case.Insensitive);
        cookie.ShouldContain("secure", Case.Insensitive);
        cookie.ShouldContain("samesite=strict", Case.Insensitive);
        cookie.ShouldContain($"path={RefreshCookie.Path}", Case.Insensitive);

        // SEC-02: nothing secret leaves in the body.
        var raw = await response.Content.ReadAsStringAsync();
        raw.ShouldNotContain("passwordHash", Case.Insensitive);
        raw.ShouldNotContain("mfaSecret", Case.Insensitive);
        raw.ShouldNotContain(ReadRefreshCookie(response)!);
    }

    [Fact]
    public async Task Login_AcceptsEmailAsWellAsUserName()
    {
        var user = await SeedUserAsync(factory);
        using var client = factory.CreateApiClient();

        var response = await LoginAsync(client, user.Email, DefaultPassword);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // SEC-15: unknown principal, wrong credential, inactive and locked accounts are indistinguishable.
    [Fact]
    public async Task Login_FailureResponses_AreIndistinguishable()
    {
        var user = await SeedUserAsync(factory);
        var inactive = await SeedUserAsync(factory, active: false);
        using var client = factory.CreateApiClient();

        var unknown = await LoginAsync(client, "no-such-user", DefaultPassword);
        var wrongPassword = await LoginAsync(client, user.UserName, "wrong-password");
        var inactiveUser = await LoginAsync(client, inactive.UserName, DefaultPassword);

        foreach (var response in new[] { unknown, wrongPassword, inactiveUser })
        {
            response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            ReadRefreshCookie(response).ShouldBeNull();
        }

        var bodies = await Task.WhenAll(new[] { unknown, wrongPassword, inactiveUser }.Select(r => r.Content.ReadAsStringAsync()));
        bodies.Distinct().Count().ShouldBe(1, "all failure bodies must be identical");
        bodies[0].ShouldContain(AuthenticationErrors.InvalidCredentials.Code);
    }

    // SEC-14: consecutive failures lock the account for the configured duration; SEC-30: lockout is audited.
    [Fact]
    public async Task Login_AfterThresholdFailures_LocksAccountAndRejectsCorrectPassword()
    {
        var user = await SeedUserAsync(factory);
        using var client = factory.CreateApiClient();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            (await LoginAsync(client, user.UserName, "wrong-password")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        }

        var lockedResponse = await LoginAsync(client, user.UserName, DefaultPassword);
        lockedResponse.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var reloaded = await ReloadUserAsync(factory, user.Id);
        reloaded.LockedUntilUtc.ShouldNotBeNull();
        reloaded.LockedUntilUtc!.Value.ShouldBeGreaterThan(DateTime.UtcNow);

        (await AuditEventsAsync(factory, user.Id, AuditEventTypes.AccountLockedOut)).Count.ShouldBe(1);
        (await AuditEventsAsync(factory, user.Id, AuditEventTypes.AuthenticationFailed)).Count.ShouldBe(6);
    }

    // SEC-30: success and failure are audited with actor, entity, timestamp and address (SEC-31).
    [Fact]
    public async Task Authentication_WritesAuditRecords()
    {
        var user = await SeedUserAsync(factory);
        using var client = factory.CreateApiClient();

        await LoginAsync(client, user.UserName, "wrong-password");
        await LoginAsync(client, user.UserName, DefaultPassword);

        var failed = (await AuditEventsAsync(factory, user.Id, AuditEventTypes.AuthenticationFailed)).Single();
        failed.EntityName.ShouldBe("User");
        failed.EntityId.ShouldBe(user.Id);
        failed.OccurredAtUtc.ShouldBeInRange(DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(1));

        var succeeded = (await AuditEventsAsync(factory, user.Id, AuditEventTypes.AuthenticationSucceeded)).Single();
        succeeded.EntityId.ShouldBe(user.Id);

        var anonymousFailure = await AuditEventsAsync(factory, null, AuditEventTypes.AuthenticationFailed);
        anonymousFailure.ShouldNotBeEmpty();
    }

    // SEC-10: deny by default; an endpoint without an explicit declaration is inaccessible anonymously.
    [Fact]
    public async Task Me_WithoutToken_IsUnauthorized()
    {
        using var client = factory.CreateApiClient();

        var response = await client.GetAsync("/api/v1/auth/me");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(Json);
        problem!.Status.ShouldBe(401);
    }

    [Fact]
    public async Task Me_WithToken_ReturnsPrincipalWithRolesAndPermissions()
    {
        var user = await SeedUserAsync(factory);
        using var client = factory.CreateApiClient();
        var (login, _) = await LoginExpectingSessionAsync(client, user.UserName, DefaultPassword);

        var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me").WithBearer(login.Authenticated!.AccessToken));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var principal = (await response.Content.ReadFromJsonAsync<PrincipalResponse>(Json))!;
        principal.Id.ShouldBe(user.Id);
        principal.Email.ShouldBe(user.Email);
        principal.Roles.ShouldNotBeNull();
        principal.Permissions.ShouldNotBeNull();
    }

    [Fact]
    public async Task Me_WithTamperedToken_IsUnauthorized()
    {
        var user = await SeedUserAsync(factory);
        using var client = factory.CreateApiClient();
        var (login, _) = await LoginExpectingSessionAsync(client, user.UserName, DefaultPassword);
        var tampered = login.Authenticated!.AccessToken[..^4] + "AAAA";

        var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me").WithBearer(tampered));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // SEC-07: refresh rotates the credential and invalidates its predecessor.
    [Fact]
    public async Task Refresh_RotatesCredentialAndInvalidatesPredecessor()
    {
        var user = await SeedUserAsync(factory);
        using var client = factory.CreateApiClient();
        var (_, first) = await LoginExpectingSessionAsync(client, user.UserName, DefaultPassword);

        var refreshed = await RefreshAsync(client, first);
        refreshed.StatusCode.ShouldBe(HttpStatusCode.OK);
        var second = ReadRefreshCookie(refreshed)!;
        second.ShouldNotBe(first);
        (await refreshed.Content.ReadFromJsonAsync<AuthenticationResponse>(Json))!.AccessToken.ShouldNotBeNullOrEmpty();

        var secondUse = await RefreshAsync(client, second);
        secondUse.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // SEC-07: presenting an already-invalidated credential invalidates the whole family and raises an audit event.
    [Fact]
    public async Task Refresh_WithReplayedCredential_RevokesFamilyAndAudits()
    {
        var user = await SeedUserAsync(factory);
        using var client = factory.CreateApiClient();
        var (_, first) = await LoginExpectingSessionAsync(client, user.UserName, DefaultPassword);
        var second = ReadRefreshCookie(await RefreshAsync(client, first))!;

        var replay = await RefreshAsync(client, first);
        replay.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        ReadRefreshCookieHeader(replay)!.ShouldContain("expires=", Case.Insensitive);

        (await AuditEventsAsync(factory, user.Id, AuditEventTypes.SessionReuseDetected)).Count.ShouldBe(1);

        var legitimate = await RefreshAsync(client, second);
        legitimate.StatusCode.ShouldBe(HttpStatusCode.Unauthorized, "the successor must be revoked along with the family");

        // The successor is itself now an invalidated credential, so its presentation is audited too (SEC-07).
        (await AuditEventsAsync(factory, user.Id, AuditEventTypes.SessionReuseDetected)).Count.ShouldBe(2);
    }

    [Fact]
    public async Task Refresh_WithoutCookieOrWithUnknownCookie_IsUnauthorized()
    {
        using var client = factory.CreateApiClient();

        (await client.PostAsync("/api/v1/auth/refresh", null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await RefreshAsync(client, "not-a-real-token")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // SEC-08: sessions are revocable server-side with immediate effect on refresh; SEC-30: revocation is audited.
    [Fact]
    public async Task Logout_RevokesSessionSoRefreshFails()
    {
        var user = await SeedUserAsync(factory);
        using var client = factory.CreateApiClient();
        var (login, refresh) = await LoginExpectingSessionAsync(client, user.UserName, DefaultPassword);

        var logout = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout")
            .WithBearer(login.Authenticated!.AccessToken)
            .WithRefreshCookie(refresh));
        logout.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await RefreshAsync(client, refresh)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await AuditEventsAsync(factory, user.Id, AuditEventTypes.SessionRevoked)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Refresh_ForDeactivatedUser_IsUnauthorized()
    {
        var user = await SeedUserAsync(factory);
        using var client = factory.CreateApiClient();
        var (_, refresh) = await LoginExpectingSessionAsync(client, user.UserName, DefaultPassword);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpenCampus.Identity.Infrastructure.Persistence.IdentityDbContext>();
            var tracked = await db.Users.FindAsync(user.Id);
            tracked!.Deactivate();
            await db.SaveChangesAsync();
        }

        (await RefreshAsync(client, refresh)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // SDD 18.2 / API-04: validation failures are 400 with field-keyed detail.
    [Fact]
    public async Task Login_WithEmptyFields_ReturnsFieldKeyedValidationProblem()
    {
        using var client = factory.CreateApiClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest("", ""));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(Json))!;
        problem.Errors.Keys.ShouldContain(nameof(LoginRequest.UserNameOrEmail));
        problem.Errors.Keys.ShouldContain(nameof(LoginRequest.Password));
    }

    [Fact]
    public async Task ChangePassword_RequiresCurrentPasswordAndTakesEffect()
    {
        var user = await SeedUserAsync(factory);
        using var client = factory.CreateApiClient();
        var (login, _) = await LoginExpectingSessionAsync(client, user.UserName, DefaultPassword);
        var token = login.Authenticated!.AccessToken;

        var wrong = await client.SendAsync(new HttpRequestMessage(HttpMethod.Put, "/api/v1/auth/password")
        {
            Content = JsonContent.Create(new ChangePasswordRequest("not-current", "Another-Strong-Password-2")),
        }.WithBearer(token));
        wrong.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var ok = await client.SendAsync(new HttpRequestMessage(HttpMethod.Put, "/api/v1/auth/password")
        {
            Content = JsonContent.Create(new ChangePasswordRequest(DefaultPassword, "Another-Strong-Password-2")),
        }.WithBearer(token));
        ok.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await LoginAsync(client, user.UserName, DefaultPassword)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await LoginAsync(client, user.UserName, "Another-Strong-Password-2")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // SEC-09: with MFA enabled the credential step yields only a challenge; verification completes the session.
    [Fact]
    public async Task Mfa_EnrolmentThenLogin_RequiresVerificationBeforeSession()
    {
        var user = await SeedUserAsync(factory);
        var clock = factory.Services.GetRequiredService<TimeProvider>();
        using var client = factory.CreateApiClient();
        var (login, _) = await LoginExpectingSessionAsync(client, user.UserName, DefaultPassword);
        var token = login.Authenticated!.AccessToken;

        var enrol = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/mfa/enrolment").WithBearer(token));
        enrol.StatusCode.ShouldBe(HttpStatusCode.OK);
        var enrolment = (await enrol.Content.ReadFromJsonAsync<MfaEnrolmentResponse>(Json))!;
        enrolment.ProvisioningUri.ShouldStartWith("otpauth://totp/");

        var wrongConfirm = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/mfa/enrolment/confirm")
        {
            Content = JsonContent.Create(new MfaConfirmRequest("000000")),
        }.WithBearer(token));
        wrongConfirm.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReloadUserAsync(factory, user.Id)).MfaEnabled.ShouldBeFalse("a wrong code must not enable MFA");

        var confirm = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/mfa/enrolment/confirm")
        {
            Content = JsonContent.Create(new MfaConfirmRequest(CurrentTotpCode(enrolment.Secret, clock))),
        }.WithBearer(token));
        confirm.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Credential step now yields a challenge and no session.
        var challengeResponse = await LoginAsync(client, user.UserName, DefaultPassword);
        challengeResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        var challenge = (await challengeResponse.Content.ReadFromJsonAsync<LoginResponse>(Json))!;
        challenge.MfaRequired.ShouldBeTrue();
        challenge.Authenticated.ShouldBeNull();
        challenge.Challenge.ShouldNotBeNullOrEmpty();
        ReadRefreshCookie(challengeResponse).ShouldBeNull();

        // The challenge is not a bearer token.
        (await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me").WithBearer(challenge.Challenge!)))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var wrongCode = await client.PostAsJsonAsync("/api/v1/auth/mfa/verify", new MfaVerifyRequest(challenge.Challenge!, "000000"));
        wrongCode.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var verified = await client.PostAsJsonAsync("/api/v1/auth/mfa/verify", new MfaVerifyRequest(challenge.Challenge!, CurrentTotpCode(enrolment.Secret, clock)));
        verified.StatusCode.ShouldBe(HttpStatusCode.OK, await verified.Content.ReadAsStringAsync());
        (await verified.Content.ReadFromJsonAsync<AuthenticationResponse>(Json))!.Principal.MfaEnabled.ShouldBeTrue();
        ReadRefreshCookie(verified).ShouldNotBeNull();
    }

    [Fact]
    public async Task Mfa_Verify_WithTamperedChallenge_IsUnauthorized()
    {
        using var client = factory.CreateApiClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/mfa/verify", new MfaVerifyRequest("not-a-challenge", "123456"));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
