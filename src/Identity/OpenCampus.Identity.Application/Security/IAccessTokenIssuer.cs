namespace OpenCampus.Identity.Application.Security;

/// <summary>Issues signed access tokens (SEC-03, SEC-04). Implemented in the infrastructure layer.</summary>
public interface IAccessTokenIssuer
{
    AccessToken Issue(AccessTokenRequest request);
}

public sealed record AccessTokenRequest(
    Guid UserId,
    string UserName,
    Guid SessionId,
    IReadOnlyCollection<string> Roles,
    IReadOnlyCollection<string> Permissions);

public sealed record AccessToken(string Value, DateTime ExpiresAtUtc);
