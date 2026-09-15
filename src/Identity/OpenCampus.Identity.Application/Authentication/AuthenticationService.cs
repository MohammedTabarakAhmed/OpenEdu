using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenCampus.Identity.Application.Abstractions;
using OpenCampus.Identity.Application.Security;
using OpenCampus.Identity.Domain.Audit;
using OpenCampus.Identity.Domain.Sessions;
using OpenCampus.Identity.Domain.Users;
using OpenCampus.SharedKernel;

namespace OpenCampus.Identity.Application.Authentication;

/// <summary>
/// Use cases for the authentication and session capability (SDD 15.3). Implements
/// SEC-05 to SEC-09 and SEC-14/15 and raises the audit events of SEC-30.
/// </summary>
public sealed class AuthenticationService(
    IUserRepository users,
    IUserSessionRepository sessions,
    IAuditEventRepository audit,
    IIdentityUnitOfWork unitOfWork,
    IPasswordHasher passwordHasher,
    IAccessTokenIssuer accessTokens,
    IRefreshTokenGenerator refreshTokens,
    IMfaChallengeIssuer mfaChallenges,
    ITotpService totp,
    IOptions<TokenOptions> tokenOptions,
    IOptions<AccountProtectionOptions> protectionOptions,
    TimeProvider clock,
    ILogger<AuthenticationService> logger)
{
    private const string UserEntity = nameof(User);
    private const string SessionEntity = nameof(UserSession);

    // Verified against when the principal is unknown so that timing does not reveal existence (SEC-15).
    private static string? _decoyHash;

    public async Task<Result<LoginOutcome>> LoginAsync(LoginRequest request, ClientContext client, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var user = await users.FindByUserNameOrEmailAsync(request.UserNameOrEmail, cancellationToken);

        if (user is null)
        {
            _decoyHash ??= passwordHasher.Hash(Guid.NewGuid().ToString("N"));
            passwordHasher.Verify(request.Password, _decoyHash);
            await RecordFailureAsync(null, client, now, "unknown_principal", cancellationToken);
            return Result.Failure<LoginOutcome>(AuthenticationErrors.InvalidCredentials);
        }

        if (!user.IsActive)
        {
            await RecordFailureAsync(user.Id, client, now, "inactive", cancellationToken);
            return Result.Failure<LoginOutcome>(AuthenticationErrors.InvalidCredentials);
        }

        if (user.IsLockedOut(now))
        {
            await RecordFailureAsync(user.Id, client, now, "locked_out", cancellationToken);
            return Result.Failure<LoginOutcome>(AuthenticationErrors.InvalidCredentials);
        }

        var verification = passwordHasher.Verify(request.Password, user.PasswordHash);
        if (verification == PasswordVerificationResult.Failed)
        {
            await RecordFailedAttemptAsync(user, client, now, "incorrect_credential", cancellationToken);
            return Result.Failure<LoginOutcome>(AuthenticationErrors.InvalidCredentials);
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.ChangePassword(passwordHasher.Hash(request.Password));
        }

        user.RecordSuccessfulLogin();

        if (user.MfaEnabled)
        {
            // SEC-09: only a short-lived challenge leaves the credential step.
            var challenge = mfaChallenges.Issue(user.Id);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Success(new LoginOutcome(null, new MfaChallengeResponse(challenge)));
        }

        var result = await IssueSessionAsync(user, client, now, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(new LoginOutcome(result, null));
    }

    public async Task<Result<AuthenticationResult>> VerifyMfaAsync(MfaVerifyRequest request, ClientContext client, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var userId = mfaChallenges.Validate(request.Challenge);
        if (userId is null)
        {
            await RecordFailureAsync(null, client, now, "invalid_mfa_challenge", cancellationToken);
            return Result.Failure<AuthenticationResult>(AuthenticationErrors.InvalidCredentials);
        }

        var user = await users.FindByIdAsync(userId.Value, cancellationToken);
        if (user is null || !user.IsActive || !user.MfaEnabled || user.MfaSecret is null)
        {
            await RecordFailureAsync(userId, client, now, "mfa_not_applicable", cancellationToken);
            return Result.Failure<AuthenticationResult>(AuthenticationErrors.InvalidCredentials);
        }

        if (user.IsLockedOut(now))
        {
            await RecordFailureAsync(user.Id, client, now, "locked_out", cancellationToken);
            return Result.Failure<AuthenticationResult>(AuthenticationErrors.InvalidCredentials);
        }

        if (!totp.Verify(user.MfaSecret, request.Code, now))
        {
            await RecordFailedAttemptAsync(user, client, now, "incorrect_mfa_code", cancellationToken);
            return Result.Failure<AuthenticationResult>(AuthenticationErrors.InvalidCredentials);
        }

        user.RecordSuccessfulLogin();
        var result = await IssueSessionAsync(user, client, now, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(result);
    }

    public async Task<Result<AuthenticationResult>> RefreshAsync(string refreshToken, ClientContext client, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var session = await sessions.FindByTokenHashAsync(refreshTokens.Hash(refreshToken), cancellationToken);

        if (session is null)
        {
            return Result.Failure<AuthenticationResult>(AuthenticationErrors.InvalidSession);
        }

        if (session.IsRevoked)
        {
            // SEC-07: a credential that was already rotated or revoked has been replayed.
            await RevokeFamilyAsync(session.FamilyId, now, cancellationToken);
            audit.Add(AuditEvent.Record(
                AuditEventTypes.SessionReuseDetected, SessionEntity, session.Id, session.UserId, now, client.IpAddress,
                Details(new { session.FamilyId })));
            await unitOfWork.SaveChangesAsync(cancellationToken);
            logger.LogWarning("Refresh credential reuse detected for session family {FamilyId}", session.FamilyId);
            return Result.Failure<AuthenticationResult>(AuthenticationErrors.InvalidSession);
        }

        if (!session.IsUsable(now))
        {
            return Result.Failure<AuthenticationResult>(AuthenticationErrors.InvalidSession);
        }

        var user = await users.FindByIdAsync(session.UserId, cancellationToken);
        if (user is null || !user.IsActive)
        {
            await RevokeFamilyAsync(session.FamilyId, now, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Failure<AuthenticationResult>(AuthenticationErrors.InvalidSession);
        }

        var replacement = refreshTokens.Generate();
        var successor = session.Rotate(replacement.Hash, now, tokenOptions.Value.RefreshTokenLifetime, client.IpAddress, client.UserAgent);
        sessions.Add(successor);

        var response = await BuildResponseAsync(user, successor.Id, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new AuthenticationResult(response, replacement.Value, successor.ExpiresAtUtc));
    }

    /// <summary>Terminates the presented session's family (SEC-08). Always succeeds so that logout is idempotent.</summary>
    public async Task LogoutAsync(string? refreshToken, ClientContext client, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(refreshToken))
        {
            return;
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var session = await sessions.FindByTokenHashAsync(refreshTokens.Hash(refreshToken), cancellationToken);
        if (session is null)
        {
            return;
        }

        var revoked = await RevokeFamilyAsync(session.FamilyId, now, cancellationToken);
        if (revoked > 0)
        {
            audit.Add(AuditEvent.Record(
                AuditEventTypes.SessionRevoked, SessionEntity, session.Id, session.UserId, now, client.IpAddress,
                Details(new { session.FamilyId, reason = "logout" })));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<Result<PrincipalResponse>> GetPrincipalAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(userId, cancellationToken);
        if (user is null || !user.IsActive)
        {
            return Result.Failure<PrincipalResponse>(AuthenticationErrors.InvalidSession);
        }

        return Result.Success(await BuildPrincipalAsync(user, cancellationToken));
    }

    public async Task<Result> ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(userId, cancellationToken);
        if (user is null || !user.IsActive)
        {
            return Result.Failure(AuthenticationErrors.InvalidSession);
        }

        if (passwordHasher.Verify(request.CurrentPassword, user.PasswordHash) == PasswordVerificationResult.Failed)
        {
            return Result.Failure(AuthenticationErrors.CurrentPasswordRejected);
        }

        user.ChangePassword(passwordHasher.Hash(request.NewPassword));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<MfaEnrolmentResponse>> BeginMfaEnrolmentAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(userId, cancellationToken);
        if (user is null || !user.IsActive)
        {
            return Result.Failure<MfaEnrolmentResponse>(AuthenticationErrors.InvalidSession);
        }

        if (user.MfaEnabled)
        {
            return Result.Failure<MfaEnrolmentResponse>(AuthenticationErrors.MfaAlreadyEnabled);
        }

        var secret = totp.GenerateSecret();
        user.BeginMfaEnrolment(secret);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new MfaEnrolmentResponse(secret, totp.BuildProvisioningUri(secret, user.UserName, tokenOptions.Value.Issuer)));
    }

    public async Task<Result> ConfirmMfaEnrolmentAsync(Guid userId, MfaConfirmRequest request, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(userId, cancellationToken);
        if (user is null || !user.IsActive)
        {
            return Result.Failure(AuthenticationErrors.InvalidSession);
        }

        if (user.MfaEnabled)
        {
            return Result.Failure(AuthenticationErrors.MfaAlreadyEnabled);
        }

        if (user.MfaSecret is null)
        {
            return Result.Failure(AuthenticationErrors.MfaNotPending);
        }

        if (!totp.Verify(user.MfaSecret, request.Code, clock.GetUtcNow().UtcDateTime))
        {
            return Result.Failure(AuthenticationErrors.MfaCodeRejected);
        }

        user.ConfirmMfaEnrolment();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private async Task<AuthenticationResult> IssueSessionAsync(User user, ClientContext client, DateTime now, CancellationToken cancellationToken)
    {
        var refresh = refreshTokens.Generate();
        var session = UserSession.Start(user.Id, refresh.Hash, now, tokenOptions.Value.RefreshTokenLifetime, client.IpAddress, client.UserAgent);
        sessions.Add(session);

        audit.Add(AuditEvent.Record(AuditEventTypes.AuthenticationSucceeded, UserEntity, user.Id, user.Id, now, client.IpAddress));

        var response = await BuildResponseAsync(user, session.Id, cancellationToken);
        return new AuthenticationResult(response, refresh.Value, session.ExpiresAtUtc);
    }

    private async Task<AuthenticationResponse> BuildResponseAsync(User user, Guid sessionId, CancellationToken cancellationToken)
    {
        var principal = await BuildPrincipalAsync(user, cancellationToken);
        var access = accessTokens.Issue(new AccessTokenRequest(user.Id, user.UserName, sessionId, principal.Roles, principal.Permissions));
        return new AuthenticationResponse(access.Value, access.ExpiresAtUtc, principal);
    }

    private async Task<PrincipalResponse> BuildPrincipalAsync(User user, CancellationToken cancellationToken)
    {
        var roles = await users.GetRoleNamesAsync(user.Id, cancellationToken);
        var permissions = await users.GetPermissionCodesAsync(user.Id, cancellationToken);
        return new PrincipalResponse(user.Id, user.UserName, user.Email, user.FullNameEn, user.FullNameAr, user.MfaEnabled, roles, permissions);
    }

    /// <summary>Counts a failed attempt against the account and records lockout when the threshold is reached (SEC-14).</summary>
    private async Task RecordFailedAttemptAsync(User user, ClientContext client, DateTime now, string reason, CancellationToken cancellationToken)
    {
        var options = protectionOptions.Value;
        var lockedOut = user.RecordFailedLogin(now, options.LockoutThreshold, options.LockoutDuration);

        audit.Add(AuditEvent.Record(AuditEventTypes.AuthenticationFailed, UserEntity, user.Id, user.Id, now, client.IpAddress, Details(new { reason })));
        if (lockedOut)
        {
            audit.Add(AuditEvent.Record(
                AuditEventTypes.AccountLockedOut, UserEntity, user.Id, user.Id, now, client.IpAddress,
                Details(new { lockedUntilUtc = user.LockedUntilUtc })));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task RecordFailureAsync(Guid? userId, ClientContext client, DateTime now, string reason, CancellationToken cancellationToken)
    {
        audit.Add(AuditEvent.Record(AuditEventTypes.AuthenticationFailed, UserEntity, userId, userId, now, client.IpAddress, Details(new { reason })));
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task<int> RevokeFamilyAsync(Guid familyId, DateTime now, CancellationToken cancellationToken)
    {
        var revoked = 0;
        foreach (var member in await sessions.GetFamilyAsync(familyId, cancellationToken))
        {
            if (!member.IsRevoked)
            {
                member.Revoke(now);
                revoked++;
            }
        }

        return revoked;
    }

    private static string Details<T>(T value) => JsonSerializer.Serialize(value);
}
