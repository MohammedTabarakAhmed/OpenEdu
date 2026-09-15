using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using OpenCampus.Identity.Application.Abstractions;

namespace OpenCampus.Api.Security;

/// <summary>Resolves the principal of the current request from the validated bearer token.</summary>
public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public const string SessionClaim = "sid";

    public Guid? UserId => Parse(accessor.HttpContext?.User, JwtRegisteredClaimNames.Sub);

    public Guid? SessionId => Parse(accessor.HttpContext?.User, SessionClaim);

    private static Guid? Parse(ClaimsPrincipal? principal, string claimType) =>
        Guid.TryParse(principal?.FindFirstValue(claimType), out var id) ? id : null;
}
