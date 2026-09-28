using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenCampus.Identity.Application.Abstractions;
using OpenCampus.Identity.Application.Administration;
using OpenCampus.Identity.Application.Authorization;
using OpenCampus.Identity.Application.External;
using OpenCampus.Identity.Application.Registration;
using OpenCampus.Identity.Application.Security;
using OpenCampus.Identity.Domain.Audit;
using OpenCampus.Identity.Domain.Roles;
using OpenCampus.Identity.Domain.Users;

namespace OpenCampus.UnitTests.Identity.Application;

/// <summary>In-memory collaborators so self-registration is tested without a database, disk or PBKDF2.</summary>
internal sealed class FakeUserRepository : IUserRepository, IIdentityUnitOfWork
{
    public List<User> Users { get; } = [];

    public int SaveCount { get; private set; }

    public Task<User?> FindByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Users.FirstOrDefault(u => u.Id == id));

    public Task<User?> FindByUserNameOrEmailAsync(string value, CancellationToken cancellationToken)
    {
        var v = value.Trim();
        return Task.FromResult(Users.FirstOrDefault(u =>
            string.Equals(u.UserName, v, StringComparison.OrdinalIgnoreCase) || string.Equals(u.Email, v, StringComparison.OrdinalIgnoreCase)));
    }

    public Task<bool> EmailExistsAsync(string email, Guid? excludingUserId, CancellationToken cancellationToken) =>
        Task.FromResult(Users.Any(u => string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase) && u.Id != excludingUserId));

    public Task<bool> UserNameExistsAsync(string userName, CancellationToken cancellationToken) =>
        Task.FromResult(Users.Any(u => string.Equals(u.UserName, userName, StringComparison.OrdinalIgnoreCase)));

    public Task<IReadOnlyCollection<string>> GetRoleNamesAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyCollection<string>>([]);

    public Task<IReadOnlyCollection<string>> GetPermissionCodesAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyCollection<string>>([]);

    public Task<PagedResult<User>> ListAsync(UserQuery query, CancellationToken cancellationToken)
    {
        var items = Users.Where(u => query.RegistrationStatus is null || u.RegistrationStatus == query.RegistrationStatus).ToList();
        return Task.FromResult(new PagedResult<User>(items, query.Page, query.PageSize, items.Count));
    }

    public Task<IReadOnlyList<User>> FindManyAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<User>>(Users.Where(u => ids.Contains(u.Id)).ToList());

    public Task<IReadOnlyList<User>> ListActiveInRoleAsync(string roleName, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<User>>([]);

    public Task<IReadOnlyList<Guid>> SearchIdsAsync(string term, int limit, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Guid>>([]);

    public void Add(User user) => Users.Add(user);

    public Task<User?> FindByVerificationTokenHashAsync(string tokenHash, CancellationToken cancellationToken) =>
        Task.FromResult(Users.FirstOrDefault(u => u.VerificationTokenHash == tokenHash));

    public Task<User?> FindByEmailAsync(string email, CancellationToken cancellationToken) =>
        Task.FromResult(Users.FirstOrDefault(u => string.Equals(u.Email, email.Trim(), StringComparison.OrdinalIgnoreCase)));

    public void Remove(User user) => Users.Remove(user);

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveCount++;
        return Task.CompletedTask;
    }
}

internal sealed class FakeRoleRepository : IRoleRepository
{
    public List<Role> Roles { get; } = RoleNames.All.Select(n => Role.Create(n, RoleNames.Descriptions[n])).ToList();

    public Role this[string name] => Roles.Single(r => r.Name == name);

    public Task<Role?> FindByIdAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult(Roles.FirstOrDefault(r => r.Id == id));

    public Task<Role?> FindByNameAsync(string name, CancellationToken cancellationToken) =>
        Task.FromResult(Roles.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase)));

    public Task<IReadOnlyList<Role>> GetAllAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Role>>(Roles);

    public void Add(Role role) => Roles.Add(role);
}

internal sealed class FakePermissionRepository : IPermissionRepository
{
    public Task<IReadOnlyList<OpenCampus.Identity.Domain.Permissions.Permission>> GetAllAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<OpenCampus.Identity.Domain.Permissions.Permission>>([]);

    public void Add(OpenCampus.Identity.Domain.Permissions.Permission permission)
    {
    }
}

internal sealed class FakeSessionRepository : IUserSessionRepository
{
    public Task<OpenCampus.Identity.Domain.Sessions.UserSession?> FindByIdAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<OpenCampus.Identity.Domain.Sessions.UserSession?>(null);

    public Task<OpenCampus.Identity.Domain.Sessions.UserSession?> FindByTokenHashAsync(string refreshTokenHash, CancellationToken cancellationToken) => Task.FromResult<OpenCampus.Identity.Domain.Sessions.UserSession?>(null);

    public Task<IReadOnlyList<OpenCampus.Identity.Domain.Sessions.UserSession>> GetFamilyAsync(Guid familyId, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<OpenCampus.Identity.Domain.Sessions.UserSession>>([]);

    public Task<IReadOnlyList<OpenCampus.Identity.Domain.Sessions.UserSession>> GetActiveForUserAsync(Guid userId, DateTime utcNow, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<OpenCampus.Identity.Domain.Sessions.UserSession>>([]);

    public void Add(OpenCampus.Identity.Domain.Sessions.UserSession session)
    {
    }
}

internal sealed class FakeAuditEvents : IAuditEventRepository
{
    public List<AuditEvent> Events { get; } = [];

    public IEnumerable<AuditEvent> OfType(string eventType) => Events.Where(e => e.EventType == eventType);

    public void Add(AuditEvent auditEvent) => Events.Add(auditEvent);

    public Task<PagedResult<AuditEvent>> ListAsync(AuditQuery query, CancellationToken cancellationToken) =>
        Task.FromResult(new PagedResult<AuditEvent>(Events, 1, Events.Count, Events.Count));
}

internal sealed class FakeEmail : IEmailDispatcher
{
    public List<EmailMessage> Sent { get; } = [];

    public IEnumerable<EmailMessage> To(string address) => Sent.Where(m => m.To.Contains(address, StringComparer.OrdinalIgnoreCase));

    public Task<ExternalResult> SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        Sent.Add(message);
        return Task.FromResult(ExternalResult.Success($"fake-{Sent.Count}"));
    }
}

internal sealed class FakeLearnerRecords : ILearnerRecordProvisioner
{
    public List<Guid> Provisioned { get; } = [];

    public Exception? Throws { get; set; }

    public Task EnsureAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (Throws is not null)
        {
            throw Throws;
        }

        Provisioned.Add(userId);
        return Task.CompletedTask;
    }
}

/// <summary>Reversible "hash" so tests can assert the clear password never appears while still recognising the hash.</summary>
internal sealed class FakePasswordHasher : IPasswordHasher
{
    public const string Prefix = "HASHED::";

    public string Hash(string password) => Prefix + new string(password.Reverse().ToArray());

    public PasswordVerificationResult Verify(string password, string storedHash) =>
        storedHash == Hash(password) ? PasswordVerificationResult.Success : PasswordVerificationResult.Failed;
}

/// <summary>Deterministic tokens ("token-1", "token-2", …) with a recognisable hash.</summary>
internal sealed class FakeTokenGenerator : IRefreshTokenGenerator
{
    private int _issued;

    public RefreshToken Generate()
    {
        var value = $"token-{++_issued}";
        return new RefreshToken(value, Hash(value));
    }

    public string Hash(string value) => "sha256:" + value;
}

internal sealed class AdjustableClock(DateTime utcNow) : TimeProvider
{
    private DateTime _utcNow = utcNow;

    public override DateTimeOffset GetUtcNow() => new(_utcNow, TimeSpan.Zero);

    public void Advance(TimeSpan by) => _utcNow = _utcNow.Add(by);
}

internal sealed class FakeCurrentUser : ICurrentUser
{
    public Guid? UserId { get; set; } = Guid.NewGuid();

    public Guid? SessionId { get; set; }

}

/// <summary>Everything a registration or administration scenario needs, wired once.</summary>
internal sealed class RegistrationHarness
{
    public static readonly DateTime Start = new(2026, 9, 20, 9, 0, 0, DateTimeKind.Utc);

    public FakeUserRepository Users { get; } = new();
    public FakeRoleRepository Roles { get; } = new();
    public FakeAuditEvents Audit { get; } = new();
    public FakeEmail Email { get; } = new();
    public FakeLearnerRecords LearnerRecords { get; } = new();
    public FakeTokenGenerator Tokens { get; } = new();
    public FakePasswordHasher Hasher { get; } = new();
    public FakeCurrentUser CurrentUser { get; } = new();
    public AdjustableClock Clock { get; } = new(Start);
    public RegistrationOptions Options { get; } = new();
    public ClientContext Client { get; } = new("203.0.113.7", "xunit");

    public RegistrationNotifier Notifier => new(Email, Microsoft.Extensions.Options.Options.Create(Options), NullLogger<RegistrationNotifier>.Instance);

    public RegistrationService Registration => new(
        Users, Roles, Audit, Users, Hasher, Tokens, Notifier, LearnerRecords,
        Microsoft.Extensions.Options.Options.Create(Options), Clock, NullLogger<RegistrationService>.Instance);

    public UserAdministrationService Administration => new(
        Users, Roles, new FakePermissionRepository(), new FakeSessionRepository(), Audit, Users, Hasher, CurrentUser, Clock,
        Notifier, LearnerRecords, NullLogger<UserAdministrationService>.Instance);

    public static RegisterRequest Request(string accountType = "Learner", string userName = "newbie", string email = "newbie@example.org") =>
        new(accountType, userName, email, "Correct-Horse-Battery-Staple-1", "New Person", "شخص جديد");

    /// <summary>The verification token carried by the most recent message to the address.</summary>
    public string TokenSentTo(string email)
    {
        var body = Email.To(email).Last().Body;
        var marker = "verify-email?token=";
        var start = body.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        var end = body.IndexOfAny(['\r', '\n', ' '], start);
        return Uri.UnescapeDataString(body[start..(end < 0 ? body.Length : end)]);
    }
}
