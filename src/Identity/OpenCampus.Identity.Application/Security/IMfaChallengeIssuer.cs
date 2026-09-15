namespace OpenCampus.Identity.Application.Security;

/// <summary>
/// SEC-09: where MFA is enabled, the credential step yields only a short-lived challenge,
/// never an access token. The challenge is opaque to the client and bound to one user.
/// </summary>
public interface IMfaChallengeIssuer
{
    string Issue(Guid userId);

    /// <summary>Returns the bound user id, or null when the challenge is invalid, tampered or expired.</summary>
    Guid? Validate(string challenge);
}
