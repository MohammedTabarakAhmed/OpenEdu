using OpenCampus.SharedKernel;

namespace OpenCampus.Identity.Application.Authentication;

public static class AuthenticationErrors
{
    /// <summary>
    /// SEC-15: the single failure returned for unknown principal, incorrect credential,
    /// locked account, inactive account and failed multi-factor verification alike.
    /// </summary>
    public static readonly Error InvalidCredentials =
        Error.Unauthorized("auth.invalid_credentials", "The credentials supplied were not accepted.");

    public static readonly Error InvalidSession =
        Error.Unauthorized("auth.invalid_session", "The session is not valid.");

    public static readonly Error MfaNotPending =
        Error.Rule("auth.mfa_not_pending", "No multi-factor enrolment is pending.");

    public static readonly Error MfaAlreadyEnabled =
        Error.Rule("auth.mfa_already_enabled", "Multi-factor authentication is already enabled.");

    public static readonly Error MfaCodeRejected =
        Error.Validation("auth.mfa_code_rejected", "The verification code was not accepted.");

    public static readonly Error CurrentPasswordRejected =
        Error.Validation("auth.current_password_rejected", "The current password was not accepted.");
}
