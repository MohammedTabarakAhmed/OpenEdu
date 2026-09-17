namespace OpenCampus.Lms.Application.Abstractions;

/// <summary>The authenticated principal of the current request, if any. Declared here (LR-03) and implemented by the host.</summary>
public interface ICurrentUser
{
    Guid? UserId { get; }

    /// <summary>Whether the caller holds the given Appendix C permission code; the host answers from the validated token.</summary>
    bool HasPermission(string permissionCode);
}
