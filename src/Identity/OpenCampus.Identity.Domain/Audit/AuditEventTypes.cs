namespace OpenCampus.Identity.Domain.Audit;

/// <summary>Event types mandated by SEC-30 for the Identity module.</summary>
public static class AuditEventTypes
{
    public const string AuthenticationSucceeded = "authentication.succeeded";
    public const string AuthenticationFailed = "authentication.failed";
    public const string AccountLockedOut = "account.locked_out";
    public const string RoleAssigned = "role.assigned";
    public const string RoleRemoved = "role.removed";
    public const string PermissionGranted = "permission.granted";
    public const string PermissionRevoked = "permission.revoked";
    public const string SessionRevoked = "session.revoked";
    public const string SessionReuseDetected = "session.reuse_detected";
    public const string UserDeactivated = "user.deactivated";
}
