using OpenCampus.Identity.Application.Administration;
using OpenCampus.SharedKernel;

namespace OpenCampus.Identity.Application.Registration;

public static class RegistrationErrors
{
    /// <summary>The feature is switched off in this environment; answered as 404 so the routes appear absent.</summary>
    public static readonly Error Disabled = Error.NotFound("registration.disabled", "Self-registration is not available.");

    /// <summary>User names are not secret, so a clash is reported plainly (409) — unlike e-mail addresses.</summary>
    public static readonly Error UserNameTaken = AdministrationErrors.UserNameTaken;

    /// <summary>One answer for an unknown, consumed or expired token: the caller learns nothing about which.</summary>
    public static readonly Error LinkInvalid = Error.NotFound("registration.link_invalid", "The verification link is not valid or has expired.");

    public static readonly Error NotPending = Error.Rule("registration.not_pending", "The account has no registration awaiting approval.");

    public static readonly Error NotVerified = Error.Rule("registration.not_verified", "The registrant has not yet verified the e-mail address.");
}
