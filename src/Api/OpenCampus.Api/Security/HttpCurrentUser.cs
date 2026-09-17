using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using OpenCampus.Identity.Infrastructure.Security;
namespace OpenCampus.Api.Security;

/// <summary>
/// Resolves the principal of the current request from the validated bearer token. Each module declares its
/// own current-user abstraction (LR-03, MB-04); the host satisfies all of them with this one implementation.
/// </summary>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor)
    : OpenCampus.Identity.Application.Abstractions.ICurrentUser, OpenCampus.Sis.Application.Abstractions.ICurrentUser, OpenCampus.Lms.Application.Abstractions.ICurrentUser
{
    public const string SessionClaim = "sid";

    public Guid? UserId => Parse(accessor.HttpContext?.User, JwtRegisteredClaimNames.Sub);

    public Guid? SessionId => Parse(accessor.HttpContext?.User, SessionClaim);

    /// <summary>Answered from the permission claims of the validated token, the same source the SEC-11 policies use.</summary>
    public bool HasPermission(string permissionCode) =>
        accessor.HttpContext?.User.HasClaim(JwtAccessTokenIssuer.PermissionClaim, permissionCode) == true;

    private static Guid? Parse(ClaimsPrincipal? principal, string claimType) =>
        Guid.TryParse(principal?.FindFirstValue(claimType), out var id) ? id : null;
}
