using OpenCampus.SharedKernel;

namespace OpenCampus.Identity.Domain.Permissions;

/// <summary>A permission code of the form module.resource.action (SDD Appendix C).</summary>
public sealed class Permission : Entity
{
    public const int CodeMaxLength = 100;
    public const int DescriptionMaxLength = 256;

    private Permission()
    {
    }

    public string Code { get; private set; } = null!;

    public string Description { get; private set; } = null!;

    public static Permission Create(string code, string description)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new DomainException("Permission code is required.");
        }

        var trimmed = code.Trim();
        var parts = trimmed.Split('.');
        if (parts.Length != 3 || parts.Any(string.IsNullOrWhiteSpace))
        {
            throw new DomainException("Permission code must have the form module.resource.action.");
        }

        return new Permission
        {
            Code = trimmed,
            Description = description?.Trim() ?? string.Empty,
        };
    }
}
