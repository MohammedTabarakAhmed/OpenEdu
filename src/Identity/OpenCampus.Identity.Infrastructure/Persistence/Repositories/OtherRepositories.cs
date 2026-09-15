using Microsoft.EntityFrameworkCore;
using OpenCampus.Identity.Application.Abstractions;
using OpenCampus.Identity.Domain.Audit;
using OpenCampus.Identity.Domain.Permissions;
using OpenCampus.Identity.Domain.Roles;
using OpenCampus.Identity.Domain.Sessions;

namespace OpenCampus.Identity.Infrastructure.Persistence.Repositories;

internal sealed class UserSessionRepository(IdentityDbContext db) : IUserSessionRepository
{
    public Task<UserSession?> FindByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.UserSessions.SingleOrDefaultAsync(s => s.Id == id, cancellationToken);

    public Task<UserSession?> FindByTokenHashAsync(string refreshTokenHash, CancellationToken cancellationToken) =>
        db.UserSessions.SingleOrDefaultAsync(s => s.RefreshTokenHash == refreshTokenHash, cancellationToken);

    public async Task<IReadOnlyList<UserSession>> GetFamilyAsync(Guid familyId, CancellationToken cancellationToken) =>
        await db.UserSessions.Where(s => s.FamilyId == familyId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<UserSession>> GetActiveForUserAsync(Guid userId, DateTime utcNow, CancellationToken cancellationToken) =>
        await db.UserSessions
            .Where(s => s.UserId == userId && s.RevokedAtUtc == null && s.ExpiresAtUtc > utcNow)
            .OrderByDescending(s => s.IssuedAtUtc)
            .ToListAsync(cancellationToken);

    public void Add(UserSession session) => db.UserSessions.Add(session);
}

internal sealed class RoleRepository(IdentityDbContext db) : IRoleRepository
{
    public Task<Role?> FindByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.Roles.Include(r => r.Permissions).SingleOrDefaultAsync(r => r.Id == id, cancellationToken);

    public Task<Role?> FindByNameAsync(string name, CancellationToken cancellationToken) =>
        db.Roles.Include(r => r.Permissions).SingleOrDefaultAsync(r => r.Name == name, cancellationToken);

    public async Task<IReadOnlyList<Role>> GetAllAsync(CancellationToken cancellationToken) =>
        await db.Roles.Include(r => r.Permissions).OrderBy(r => r.Name).ToListAsync(cancellationToken);

    public void Add(Role role) => db.Roles.Add(role);
}

internal sealed class PermissionRepository(IdentityDbContext db) : IPermissionRepository
{
    public async Task<IReadOnlyList<Permission>> GetAllAsync(CancellationToken cancellationToken) =>
        await db.Permissions.OrderBy(p => p.Code).ToListAsync(cancellationToken);

    public void Add(Permission permission) => db.Permissions.Add(permission);
}

internal sealed class AuditEventRepository(IdentityDbContext db) : IAuditEventRepository
{
    public void Add(AuditEvent auditEvent) => db.AuditEvents.Add(auditEvent);

    public async Task<PagedResult<AuditEvent>> ListAsync(AuditQuery query, CancellationToken cancellationToken)
    {
        IQueryable<AuditEvent> source = db.AuditEvents;

        if (query.UserId is { } userId)
        {
            source = source.Where(a => a.UserId == userId);
        }

        if (!string.IsNullOrWhiteSpace(query.EventType))
        {
            source = source.Where(a => a.EventType == query.EventType);
        }

        if (query.FromUtc is { } from)
        {
            source = source.Where(a => a.OccurredAtUtc >= from);
        }

        if (query.ToUtc is { } to)
        {
            source = source.Where(a => a.OccurredAtUtc < to);
        }

        source = source.OrderByDescending(a => a.OccurredAtUtc);

        var total = await source.CountAsync(cancellationToken);
        var items = await source
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<AuditEvent>(items, query.Page, query.PageSize, total);
    }
}

internal sealed class IdentityUnitOfWork(IdentityDbContext db) : IIdentityUnitOfWork
{
    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
