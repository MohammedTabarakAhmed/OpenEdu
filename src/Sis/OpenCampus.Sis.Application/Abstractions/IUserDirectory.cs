namespace OpenCampus.Sis.Application.Abstractions;

/// <summary>Display identity of an Identity-module user as seen from SIS (6.5: "resolve display identity for a user reference").</summary>
public sealed record UserSummary(Guid Id, string UserName, string Email, string FullNameEn, string FullNameAr, bool IsActive, IReadOnlyList<string> Roles);

/// <summary>
/// Cross-module contract to the Identity module (MB-02): declared in the consuming module's application
/// layer, implemented in the host composition root over the Identity module. Used to validate
/// InstructorUserId and Learner.UserId references at creation (13.5) and to resolve names for display.
/// </summary>
public interface IUserDirectory
{
    Task<UserSummary?> FindAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Batch resolution so that list endpoints issue one lookup, not one per row (NFR-04).</summary>
    Task<IReadOnlyDictionary<Guid, UserSummary>> FindManyAsync(IEnumerable<Guid> userIds, CancellationToken cancellationToken);

    /// <summary>Active users holding the named role, ordered by user name.</summary>
    Task<IReadOnlyList<UserSummary>> ListActiveInRoleAsync(string roleName, CancellationToken cancellationToken);

    /// <summary>Identifiers of users whose user name, e-mail or full name contains the term (bounded), for name-based searches of SIS records.</summary>
    Task<IReadOnlyList<Guid>> SearchIdsAsync(string term, int limit, CancellationToken cancellationToken);
}
