using Microsoft.AspNetCore.Authorization;
using OpenCampus.Identity.Application.Authorization;
using OpenCampus.Identity.Infrastructure.Security;

namespace OpenCampus.Api.Security;

/// <summary>
/// SEC-11: coarse-grained authorisation as named policies bound to permission codes.
/// One policy is registered per catalogue entry; its name is the code itself.
/// </summary>
public static class PermissionAuthorization
{
    public static AuthorizationBuilder AddPermissionPolicies(this AuthorizationBuilder builder)
    {
        foreach (var code in Permissions.AllCodes)
        {
            builder.AddPolicy(code, policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim(JwtAccessTokenIssuer.PermissionClaim, code));
        }

        return builder;
    }
}

/// <summary>Declares the permission code required by an endpoint, e.g. <c>[HasPermission(Permissions.Identity.UserRead)]</c>.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class HasPermissionAttribute : AuthorizeAttribute
{
    public HasPermissionAttribute(string permissionCode)
        : base(permissionCode)
    {
        if (!Permissions.AllCodes.Contains(permissionCode))
        {
            throw new ArgumentException($"'{permissionCode}' is not a catalogued permission code.", nameof(permissionCode));
        }
    }
}
