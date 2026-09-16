using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
namespace OpenCampus.Api.Security;

/// <summary>
/// Resolves the principal of the current request from the validated bearer token. Each module declares its
/// own current-user abstraction (LR-03, MB-04); the host satisfies all of them with this one implementation.
/// </summary>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor)
    : OpenCampus.Identity.Application.Abstractions.ICurrentUser, OpenCampus.Sis.Application.Abstractions.ICurrentUser
{
    public const string SessionClaim = "sid";

    public Guid? UserId => Parse(accessor.HttpContext?.User, JwtRegisteredClaimNames.Sub);

    public Guid? SessionId => Parse(accessor.HttpContext?.User, SessionClaim);

    private static Guid? Parse(ClaimsPrincipal? principal, string claimType) =>
        Guid.TryParse(principal?.FindFirstValue(claimType), out var id) ? id : null;
}
