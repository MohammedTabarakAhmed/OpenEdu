namespace OpenCampus.Sis.Application.Abstractions;

/// <summary>The authenticated principal of the current request, if any. Declared here (LR-03) and implemented by the host.</summary>
public interface ICurrentUser
{
    Guid? UserId { get; }
}
