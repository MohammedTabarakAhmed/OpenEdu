namespace OpenCampus.Identity.Domain.Users;

/// <summary>Association between a user and a role; composite key (SDD 13.2).</summary>
public sealed class UserRole
{
    private UserRole()
    {
    }

    internal UserRole(Guid userId, Guid roleId)
    {
        UserId = userId;
        RoleId = roleId;
    }

    public Guid UserId { get; private set; }

    public Guid RoleId { get; private set; }
}
