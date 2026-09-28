namespace OpenCampus.Identity.Domain.Users;

/// <summary>
/// Where a self-registered account stands (Increment 7). <see cref="None"/> marks accounts created by an
/// administrator or the provisioner, which never pass through verification or approval.
/// </summary>
public enum RegistrationStatus
{
    None = 0,
    AwaitingVerification = 1,
    AwaitingApproval = 2,
    Approved = 3,
}
