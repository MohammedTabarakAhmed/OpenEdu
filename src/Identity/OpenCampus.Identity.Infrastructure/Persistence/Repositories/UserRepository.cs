using Microsoft.EntityFrameworkCore;
using OpenCampus.Identity.Application.Abstractions;
using OpenCampus.Identity.Domain.Users;

namespace OpenCampus.Identity.Infrastructure.Persistence.Repositories;

internal sealed class UserRepository(IdentityDbContext db) : IUserRepository
{
    public Task<User?> FindByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.Users.Include(u => u.Roles).SingleOrDefaultAsync(u => u.Id == id, cancellationToken);

    public Task<User?> FindByUserNameOrEmailAsync(string value, CancellationToken cancellationToken)
    {
        var normalised = value.Trim();
        return db.Users
            .Include(u => u.Roles)
            .SingleOrDefaultAsync(u => u.UserName == normalised || u.Email == normalised, cancellationToken);
    }

    public Task<bool> EmailExistsAsync(string email, Guid? excludingUserId, CancellationToken cancellationToken) =>
        db.Users.AnyAsync(u => u.Email == email && (excludingUserId == null || u.Id != excludingUserId), cancellationToken);

    public Task<bool> UserNameExistsAsync(string userName, CancellationToken cancellationToken) =>
        db.Users.AnyAsync(u => u.UserName == userName, cancellationToken);

    public async Task<IReadOnlyCollection<string>> GetRoleNamesAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.Set<UserRole>()
            .Where(ur => ur.UserId == userId)
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (_, r) => r.Name)
            .OrderBy(n => n)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyCollection<string>> GetPermissionCodesAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.Set<UserRole>()
            .Where(ur => ur.UserId == userId)
            .Join(db.Set<Domain.Roles.RolePermission>(), ur => ur.RoleId, rp => rp.RoleId, (_, rp) => rp.PermissionId)
            .Join(db.Permissions, id => id, p => p.Id, (_, p) => p.Code)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync(cancellationToken);

    public async Task<PagedResult<User>> ListAsync(UserQuery query, CancellationToken cancellationToken)
    {
        IQueryable<User> source = db.Users.Include(u => u.Roles);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            source = source.Where(u =>
                u.UserName.Contains(term) || u.Email.Contains(term) || u.FullNameEn.Contains(term) || u.FullNameAr.Contains(term));
        }

        if (query.IsActive is { } isActive)
        {
            source = source.Where(u => u.IsActive == isActive);
        }

        if (query.RegistrationStatus is { } registrationStatus)
        {
            source = source.Where(u => u.RegistrationStatus == registrationStatus);
        }

        source = (query.Sort?.ToLowerInvariant(), query.Descending) switch
        {
            ("email", false) => source.OrderBy(u => u.Email),
            ("email", true) => source.OrderByDescending(u => u.Email),
            ("fullnameen", false) => source.OrderBy(u => u.FullNameEn),
            ("fullnameen", true) => source.OrderByDescending(u => u.FullNameEn),
            ("createdatutc", false) => source.OrderBy(u => u.CreatedAtUtc),
            ("createdatutc", true) => source.OrderByDescending(u => u.CreatedAtUtc),
            (_, true) => source.OrderByDescending(u => u.UserName),
            _ => source.OrderBy(u => u.UserName),
        };

        var total = await source.CountAsync(cancellationToken);
        var items = await source
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<User>(items, query.Page, query.PageSize, total);
    }

    public async Task<IReadOnlyList<User>> FindManyAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        var set = ids.Distinct().ToArray();
        return await db.Users.Include(u => u.Roles).Where(u => set.Contains(u.Id)).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<User>> ListActiveInRoleAsync(string roleName, CancellationToken cancellationToken)
    {
        var roleIds = db.Roles.Where(r => r.Name == roleName).Select(r => r.Id);
        return await db.Users
            .Include(u => u.Roles)
            .Where(u => u.IsActive && u.Roles.Any(ur => roleIds.Contains(ur.RoleId)))
            .OrderBy(u => u.UserName)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> SearchIdsAsync(string term, int limit, CancellationToken cancellationToken)
    {
        var trimmed = term.Trim();
        return await db.Users
            .Where(u => u.UserName.Contains(trimmed) || u.Email.Contains(trimmed) || u.FullNameEn.Contains(trimmed) || u.FullNameAr.Contains(trimmed))
            .OrderBy(u => u.UserName)
            .Select(u => u.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public void Add(User user) => db.Users.Add(user);

    public Task<User?> FindByVerificationTokenHashAsync(string tokenHash, CancellationToken cancellationToken) =>
        db.Users.Include(u => u.Roles).SingleOrDefaultAsync(u => u.VerificationTokenHash == tokenHash, cancellationToken);

    public Task<User?> FindByEmailAsync(string email, CancellationToken cancellationToken)
    {
        var normalised = email.Trim();
        return db.Users.Include(u => u.Roles).SingleOrDefaultAsync(u => u.Email == normalised, cancellationToken);
    }

    public void Remove(User user) => db.Users.Remove(user);
}
