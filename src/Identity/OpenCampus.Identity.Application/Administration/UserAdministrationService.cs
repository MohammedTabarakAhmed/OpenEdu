using System.Text.Json;
using OpenCampus.Identity.Application.Abstractions;
using OpenCampus.Identity.Application.Security;
using OpenCampus.Identity.Domain.Audit;
using OpenCampus.Identity.Domain.Roles;
using OpenCampus.Identity.Domain.Sessions;
using OpenCampus.Identity.Domain.Users;
using OpenCampus.SharedKernel;

namespace OpenCampus.Identity.Application.Administration;

public static class AdministrationErrors
{
    public static readonly Error UserNotFound = Error.NotFound("users.not_found", "The user was not found.");
    public static readonly Error SessionNotFound = Error.NotFound("sessions.not_found", "The session was not found.");
    public static readonly Error UserNameTaken = Error.Conflict("users.user_name_taken", "The user name is already in use.");
    public static readonly Error EmailTaken = Error.Conflict("users.email_taken", "The e-mail address is already in use.");

    public static Error UnknownRole(string name) => Error.Validation("users.unknown_role", $"Role '{name}' does not exist.");
}

/// <summary>
/// User administration capability (SDD 15.3): listing, creation, retrieval, amendment,
/// deactivation, role assignment, and enumeration and revocation of sessions. Raises the
/// audit events of SEC-30 for role assignment, session revocation and deactivation.
/// </summary>
public sealed class UserAdministrationService(
    IUserRepository users,
    IRoleRepository roles,
    IPermissionRepository permissions,
    IUserSessionRepository sessions,
    IAuditEventRepository audit,
    IIdentityUnitOfWork unitOfWork,
    IPasswordHasher passwordHasher,
    ICurrentUser currentUser,
    TimeProvider clock)
{
    public static readonly IReadOnlyList<string> SortFields = ["userName", "email", "fullNameEn", "createdAtUtc"];

    public async Task<PagedResponse<UserResponse>> ListAsync(UserListQuery query, CancellationToken cancellationToken)
    {
        var (page, pageSize) = Paging.Normalise(query.Page, query.PageSize);
        var descending = query.Sort?.StartsWith('-') == true;
        var sort = query.Sort?.TrimStart('-');

        var result = await users.ListAsync(new UserQuery(page, pageSize, query.Search, query.IsActive, sort, descending), cancellationToken);
        var roleNames = await RoleNamesByIdAsync(cancellationToken);

        return new PagedResponse<UserResponse>(
            result.Items.Select(u => ToResponse(u, roleNames)).ToList(),
            result.Page,
            result.PageSize,
            result.TotalCount);
    }

    public async Task<Result<UserResponse>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(id, cancellationToken);
        return user is null
            ? Result.Failure<UserResponse>(AdministrationErrors.UserNotFound)
            : Result.Success(ToResponse(user, await RoleNamesByIdAsync(cancellationToken)));
    }

    public async Task<Result<UserResponse>> CreateAsync(CreateUserRequest request, ClientContext client, CancellationToken cancellationToken)
    {
        if (await users.UserNameExistsAsync(request.UserName.Trim(), cancellationToken))
        {
            return Result.Failure<UserResponse>(AdministrationErrors.UserNameTaken);
        }

        if (await users.EmailExistsAsync(request.Email.Trim(), null, cancellationToken))
        {
            return Result.Failure<UserResponse>(AdministrationErrors.EmailTaken);
        }

        var resolvedRoles = await ResolveRolesAsync(request.Roles, cancellationToken);
        if (resolvedRoles.IsFailure)
        {
            return Result.Failure<UserResponse>(resolvedRoles.Error);
        }

        var user = User.Create(request.Email, request.UserName, passwordHasher.Hash(request.Password), request.FullNameEn, request.FullNameAr);
        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var role in resolvedRoles.Value)
        {
            user.AssignRole(role.Id);
            audit.Add(RoleAudit(AuditEventTypes.RoleAssigned, user.Id, role.Name, now, client));
        }

        users.Add(user);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(ToResponse(user, await RoleNamesByIdAsync(cancellationToken)));
    }

    public async Task<Result<UserResponse>> UpdateAsync(Guid id, UpdateUserRequest request, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(id, cancellationToken);
        if (user is null)
        {
            return Result.Failure<UserResponse>(AdministrationErrors.UserNotFound);
        }

        if (await users.EmailExistsAsync(request.Email.Trim(), id, cancellationToken))
        {
            return Result.Failure<UserResponse>(AdministrationErrors.EmailTaken);
        }

        user.Amend(request.Email, request.FullNameEn, request.FullNameAr);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(ToResponse(user, await RoleNamesByIdAsync(cancellationToken)));
    }

    /// <summary>Deactivation is logical (DC-03): the account remains for audit and history; its sessions are revoked (SEC-08).</summary>
    public async Task<Result> DeactivateAsync(Guid id, ClientContext client, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(id, cancellationToken);
        if (user is null)
        {
            return Result.Failure(AdministrationErrors.UserNotFound);
        }

        var now = clock.GetUtcNow().UtcDateTime;
        if (user.IsActive)
        {
            user.Deactivate();
            audit.Add(AuditEvent.Record(AuditEventTypes.UserDeactivated, nameof(User), user.Id, currentUser.UserId, now, client.IpAddress));
        }

        foreach (var session in await sessions.GetActiveForUserAsync(user.Id, now, cancellationToken))
        {
            session.Revoke(now);
            audit.Add(SessionAudit(session, now, client, "user_deactivated"));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> ActivateAsync(Guid id, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(id, cancellationToken);
        if (user is null)
        {
            return Result.Failure(AdministrationErrors.UserNotFound);
        }

        user.Activate();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<UserResponse>> AssignRolesAsync(Guid id, AssignRolesRequest request, ClientContext client, CancellationToken cancellationToken)
    {
        var user = await users.FindByIdAsync(id, cancellationToken);
        if (user is null)
        {
            return Result.Failure<UserResponse>(AdministrationErrors.UserNotFound);
        }

        var resolved = await ResolveRolesAsync(request.Roles, cancellationToken);
        if (resolved.IsFailure)
        {
            return Result.Failure<UserResponse>(resolved.Error);
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var roleNames = await RoleNamesByIdAsync(cancellationToken);
        var desired = resolved.Value.Select(r => r.Id).ToHashSet();

        foreach (var current in user.Roles.Select(r => r.RoleId).ToList())
        {
            if (!desired.Contains(current) && user.RemoveRole(current))
            {
                audit.Add(RoleAudit(AuditEventTypes.RoleRemoved, user.Id, roleNames[current], now, client));
            }
        }

        foreach (var role in resolved.Value)
        {
            if (user.AssignRole(role.Id))
            {
                audit.Add(RoleAudit(AuditEventTypes.RoleAssigned, user.Id, role.Name, now, client));
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ToResponse(user, roleNames));
    }

    public async Task<Result<IReadOnlyList<SessionResponse>>> ListSessionsAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (await users.FindByIdAsync(userId, cancellationToken) is null)
        {
            return Result.Failure<IReadOnlyList<SessionResponse>>(AdministrationErrors.UserNotFound);
        }

        var active = await sessions.GetActiveForUserAsync(userId, clock.GetUtcNow().UtcDateTime, cancellationToken);
        return Result.Success<IReadOnlyList<SessionResponse>>(active.Select(ToResponse).ToList());
    }

    /// <summary>Revokes one session and, with it, every generation of its family (SEC-08).</summary>
    public async Task<Result> RevokeSessionAsync(Guid userId, Guid sessionId, ClientContext client, CancellationToken cancellationToken)
    {
        var session = await sessions.FindByIdAsync(sessionId, cancellationToken);
        if (session is null || session.UserId != userId)
        {
            return Result.Failure(AdministrationErrors.SessionNotFound);
        }

        var now = clock.GetUtcNow().UtcDateTime;
        var revoked = 0;
        foreach (var member in await sessions.GetFamilyAsync(session.FamilyId, cancellationToken))
        {
            if (!member.IsRevoked)
            {
                member.Revoke(now);
                revoked++;
            }
        }

        if (revoked > 0)
        {
            audit.Add(SessionAudit(session, now, client, "administrative"));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result> RevokeAllSessionsAsync(Guid userId, ClientContext client, CancellationToken cancellationToken)
    {
        if (await users.FindByIdAsync(userId, cancellationToken) is null)
        {
            return Result.Failure(AdministrationErrors.UserNotFound);
        }

        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var session in await sessions.GetActiveForUserAsync(userId, now, cancellationToken))
        {
            session.Revoke(now);
            audit.Add(SessionAudit(session, now, client, "administrative"));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<IReadOnlyList<RoleResponse>> ListRolesAsync(CancellationToken cancellationToken)
    {
        var codesById = (await permissions.GetAllAsync(cancellationToken)).ToDictionary(p => p.Id, p => p.Code);
        var all = await roles.GetAllAsync(cancellationToken);

        return all.Select(r => new RoleResponse(
            r.Id,
            r.Name,
            r.Description,
            r.Permissions.Select(p => codesById[p.PermissionId]).OrderBy(c => c).ToList())).ToList();
    }

    public async Task<PagedResponse<AuditEventResponse>> ListAuditAsync(AuditListQuery query, CancellationToken cancellationToken)
    {
        var (page, pageSize) = Paging.Normalise(query.Page, query.PageSize);
        var result = await audit.ListAsync(new AuditQuery(page, pageSize, query.UserId, query.EventType, query.FromUtc, query.ToUtc), cancellationToken);

        return new PagedResponse<AuditEventResponse>(
            result.Items.Select(a => new AuditEventResponse(a.Id, a.UserId, a.EventType, a.EntityName, a.EntityId, a.DetailsJson, a.OccurredAtUtc, a.IpAddress)).ToList(),
            result.Page,
            result.PageSize,
            result.TotalCount);
    }

    private async Task<Result<IReadOnlyList<Role>>> ResolveRolesAsync(IReadOnlyList<string> names, CancellationToken cancellationToken)
    {
        var all = await roles.GetAllAsync(cancellationToken);
        var resolved = new List<Role>();
        foreach (var name in names.Select(n => n.Trim()).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var role = all.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
            if (role is null)
            {
                return Result.Failure<IReadOnlyList<Role>>(AdministrationErrors.UnknownRole(name));
            }

            resolved.Add(role);
        }

        return Result.Success<IReadOnlyList<Role>>(resolved);
    }

    private async Task<IReadOnlyDictionary<Guid, string>> RoleNamesByIdAsync(CancellationToken cancellationToken) =>
        (await roles.GetAllAsync(cancellationToken)).ToDictionary(r => r.Id, r => r.Name);

    private static UserResponse ToResponse(User user, IReadOnlyDictionary<Guid, string> roleNames) => new(
        user.Id,
        user.UserName,
        user.Email,
        user.FullNameEn,
        user.FullNameAr,
        user.IsActive,
        user.MfaEnabled,
        user.LockedUntilUtc,
        user.Roles.Select(r => roleNames.TryGetValue(r.RoleId, out var name) ? name : r.RoleId.ToString()).OrderBy(n => n).ToList(),
        user.CreatedAtUtc,
        user.ModifiedAtUtc);

    private static SessionResponse ToResponse(UserSession session) =>
        new(session.Id, session.FamilyId, session.IssuedAtUtc, session.ExpiresAtUtc, session.IpAddress, session.UserAgent);

    private AuditEvent RoleAudit(string eventType, Guid userId, string roleName, DateTime now, ClientContext client) =>
        AuditEvent.Record(eventType, nameof(User), userId, currentUser.UserId, now, client.IpAddress, JsonSerializer.Serialize(new { role = roleName }));

    private AuditEvent SessionAudit(UserSession session, DateTime now, ClientContext client, string reason) =>
        AuditEvent.Record(
            AuditEventTypes.SessionRevoked, nameof(UserSession), session.Id, currentUser.UserId, now, client.IpAddress,
            JsonSerializer.Serialize(new { session.FamilyId, subjectUserId = session.UserId, reason }));
}
