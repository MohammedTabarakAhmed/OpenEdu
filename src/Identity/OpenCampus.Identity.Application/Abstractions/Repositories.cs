using OpenCampus.Identity.Domain.Audit;
using OpenCampus.Identity.Domain.Permissions;
using OpenCampus.Identity.Domain.Roles;
using OpenCampus.Identity.Domain.Sessions;
using OpenCampus.Identity.Domain.Users;

namespace OpenCampus.Identity.Application.Abstractions;

/// <summary>Persistence contracts declared by the application layer and implemented in infrastructure (LR-03).</summary>
public interface IUserRepository
{
    Task<User?> FindByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Case-insensitive match on user name or e-mail.</summary>
    Task<User?> FindByUserNameOrEmailAsync(string value, CancellationToken cancellationToken);

    Task<bool> EmailExistsAsync(string email, Guid? excludingUserId, CancellationToken cancellationToken);

    Task<bool> UserNameExistsAsync(string userName, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<string>> GetRoleNamesAsync(Guid userId, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<string>> GetPermissionCodesAsync(Guid userId, CancellationToken cancellationToken);

    Task<PagedResult<User>> ListAsync(UserQuery query, CancellationToken cancellationToken);

    // Published for cross-module display-identity resolution (6.5), implemented by the host adapter (MB-02).

    /// <summary>Users by identifier, roles included, in one query.</summary>
    Task<IReadOnlyList<User>> FindManyAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken);

    /// <summary>Active users holding the named role, ordered by user name.</summary>
    Task<IReadOnlyList<User>> ListActiveInRoleAsync(string roleName, CancellationToken cancellationToken);

    /// <summary>Identifiers of users whose user name, e-mail or full name contains the term, bounded by <paramref name="limit"/>.</summary>
    Task<IReadOnlyList<Guid>> SearchIdsAsync(string term, int limit, CancellationToken cancellationToken);

    void Add(User user);
}

public sealed record UserQuery(int Page, int PageSize, string? Search, bool? IsActive, string? Sort, bool Descending);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public interface IUserSessionRepository
{
    Task<UserSession?> FindByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<UserSession?> FindByTokenHashAsync(string refreshTokenHash, CancellationToken cancellationToken);

    Task<IReadOnlyList<UserSession>> GetFamilyAsync(Guid familyId, CancellationToken cancellationToken);

    Task<IReadOnlyList<UserSession>> GetActiveForUserAsync(Guid userId, DateTime utcNow, CancellationToken cancellationToken);

    void Add(UserSession session);
}

public interface IRoleRepository
{
    Task<Role?> FindByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<Role?> FindByNameAsync(string name, CancellationToken cancellationToken);

    Task<IReadOnlyList<Role>> GetAllAsync(CancellationToken cancellationToken);

    void Add(Role role);
}

public interface IPermissionRepository
{
    Task<IReadOnlyList<Permission>> GetAllAsync(CancellationToken cancellationToken);

    void Add(Permission permission);
}

public interface IAuditEventRepository
{
    void Add(AuditEvent auditEvent);

    Task<PagedResult<AuditEvent>> ListAsync(AuditQuery query, CancellationToken cancellationToken);
}

public sealed record AuditQuery(int Page, int PageSize, Guid? UserId, string? EventType, DateTime? FromUtc, DateTime? ToUtc);

/// <summary>Commits all pending changes of the Identity module atomically.</summary>
public interface IIdentityUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
