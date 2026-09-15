namespace OpenCampus.Identity.Application.Authentication;

// Request contracts (API-02, SEC-18): dedicated types, never persistence entities.

public sealed record LoginRequest(string UserNameOrEmail, string Password);

public sealed record MfaVerifyRequest(string Challenge, string Code);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record MfaConfirmRequest(string Code);

// Response contracts.

public sealed record PrincipalResponse(
    Guid Id,
    string UserName,
    string Email,
    string FullNameEn,
    string FullNameAr,
    bool MfaEnabled,
    IReadOnlyCollection<string> Roles,
    IReadOnlyCollection<string> Permissions);

/// <summary>Returned in the response body. The refresh credential is never part of it (SEC-05).</summary>
public sealed record AuthenticationResponse(string AccessToken, DateTime AccessTokenExpiresAtUtc, PrincipalResponse Principal);

/// <summary>Returned by the credential step when MFA is enabled (SEC-09): a challenge only, no token.</summary>
public sealed record MfaChallengeResponse(string Challenge);

/// <summary>Sole response carrying the shared secret, issued once at enrolment so the authenticator can be provisioned.</summary>
public sealed record MfaEnrolmentResponse(string Secret, string ProvisioningUri);

/// <summary>Full outcome of a successful authentication; the host moves the refresh credential into a cookie.</summary>
public sealed record AuthenticationResult(
    AuthenticationResponse Response,
    string RefreshToken,
    DateTime RefreshTokenExpiresAtUtc);

/// <summary>Outcome of the credential step: exactly one of the two members is populated.</summary>
public sealed record LoginOutcome(AuthenticationResult? Authenticated, MfaChallengeResponse? MfaChallenge)
{
    public bool RequiresMfa => MfaChallenge is not null;
}
