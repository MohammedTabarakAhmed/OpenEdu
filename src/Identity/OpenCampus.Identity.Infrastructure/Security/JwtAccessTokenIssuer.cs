using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using OpenCampus.Identity.Application.Security;

namespace OpenCampus.Identity.Infrastructure.Security;

/// <summary>SEC-03/SEC-04: RS256-signed access token with a lifetime bounded by <see cref="TokenOptions"/>.</summary>
public sealed class JwtAccessTokenIssuer(
    IOptions<TokenOptions> options,
    ISigningKeyProvider keys,
    TimeProvider timeProvider) : IAccessTokenIssuer
{
    public const string RoleClaim = "role";
    public const string PermissionClaim = "permission";
    public const string SessionClaim = "sid";

    private readonly JsonWebTokenHandler _handler = new() { SetDefaultTimesOnTokenCreation = false };

    public AccessToken Issue(AccessTokenRequest request)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var expires = now.Add(options.Value.AccessTokenLifetime);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, request.UserId.ToString()),
            new(JwtRegisteredClaimNames.UniqueName, request.UserName),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(SessionClaim, request.SessionId.ToString()),
        };
        claims.AddRange(request.Roles.Select(r => new Claim(RoleClaim, r)));
        claims.AddRange(request.Permissions.Select(p => new Claim(PermissionClaim, p)));

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = options.Value.Issuer,
            Audience = options.Value.Audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = now,
            NotBefore = now,
            Expires = expires,
            SigningCredentials = new SigningCredentials(keys.SigningKey, SecurityAlgorithms.RsaSha256),
        };

        return new AccessToken(_handler.CreateToken(descriptor), expires);
    }
}
