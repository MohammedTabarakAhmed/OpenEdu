using OpenCampus.Identity.Application.Abstractions;
using OpenCampus.Identity.Domain.Users;
using OpenCampus.Sis.Application.Abstractions;

namespace OpenCampus.Api.Integration;

/// <summary>
/// Implements the SIS module's contract to Identity (6.5, MB-02). Lives in the host because the section 12
/// reference table is exhaustive and no module may reference another module's application layer (MB-04);
/// composition of module implementations occurs only here (MB-05). Reads Identity exclusively through its
/// published repository contract, never its schema (MB-01).
/// </summary>
public sealed class IdentityUserDirectory(IUserRepository users, IRoleRepository roles) : IUserDirectory
{
    public async Task<UserSummary?> FindAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(userId, cancellationToken);
        return user is null ? null : await ToSummaryAsync(user, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, UserSummary>> FindManyAsync(IEnumerable<Guid> userIds, CancellationToken cancellationToken)
    {
        var found = await users.FindManyAsync(userIds, cancellationToken);
        var roleNames = await RoleNamesAsync(cancellationToken);
        return found.ToDictionary(u => u.Id, u => ToSummary(u, roleNames));
    }

    public async Task<IReadOnlyList<UserSummary>> ListActiveInRoleAsync(string roleName, CancellationToken cancellationToken)
    {
        var found = await users.ListActiveInRoleAsync(roleName, cancellationToken);
        var roleNames = await RoleNamesAsync(cancellationToken);
        return found.Select(u => ToSummary(u, roleNames)).ToList();
    }

    public Task<IReadOnlyList<Guid>> SearchIdsAsync(string term, int limit, CancellationToken cancellationToken) =>
        users.SearchIdsAsync(term, limit, cancellationToken);

    private async Task<UserSummary> ToSummaryAsync(User user, CancellationToken cancellationToken) =>
        ToSummary(user, await RoleNamesAsync(cancellationToken));

    private async Task<IReadOnlyDictionary<Guid, string>> RoleNamesAsync(CancellationToken cancellationToken) =>
        (await roles.GetAllAsync(cancellationToken)).ToDictionary(r => r.Id, r => r.Name);

    private static UserSummary ToSummary(User u, IReadOnlyDictionary<Guid, string> roleNames) => new(
        u.Id, u.UserName, u.Email, u.FullNameEn, u.FullNameAr, u.IsActive,
        u.Roles.Select(r => roleNames.TryGetValue(r.RoleId, out var n) ? n : r.RoleId.ToString()).OrderBy(n => n).ToList());
}
