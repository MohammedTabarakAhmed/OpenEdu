namespace OpenCampus.Identity.Domain.Roles;

/// <summary>Association between a role and a permission; composite key (SDD 13.2).</summary>
public sealed class RolePermission
{
    private RolePermission()
    {
    }

    internal RolePermission(Guid roleId, Guid permissionId)
    {
        RoleId = roleId;
        PermissionId = permissionId;
    }

    public Guid RoleId { get; private set; }

    public Guid PermissionId { get; private set; }
}
