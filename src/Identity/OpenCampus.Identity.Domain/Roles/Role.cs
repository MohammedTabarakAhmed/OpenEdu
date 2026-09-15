using OpenCampus.SharedKernel;

namespace OpenCampus.Identity.Domain.Roles;

public sealed class Role : Entity
{
    public const int NameMaxLength = 64;
    public const int DescriptionMaxLength = 256;

    private readonly List<RolePermission> _permissions = [];

    private Role()
    {
    }

    public string Name { get; private set; } = null!;

    public string Description { get; private set; } = null!;

    public IReadOnlyCollection<RolePermission> Permissions => _permissions.AsReadOnly();

    public static Role Create(string name, string description)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Role name is required.");
        }

        return new Role
        {
            Name = name.Trim(),
            Description = description?.Trim() ?? string.Empty,
        };
    }

    /// <summary>Grants a permission; idempotent. Returns true when the grant was new.</summary>
    public bool GrantPermission(Guid permissionId)
    {
        if (_permissions.Any(p => p.PermissionId == permissionId))
        {
            return false;
        }

        _permissions.Add(new RolePermission(Id, permissionId));
        return true;
    }

    public bool RevokePermission(Guid permissionId)
    {
        return _permissions.RemoveAll(p => p.PermissionId == permissionId) > 0;
    }
}
