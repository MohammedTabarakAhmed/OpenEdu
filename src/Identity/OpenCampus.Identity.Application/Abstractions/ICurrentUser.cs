namespace OpenCampus.Identity.Application.Abstractions;

/// <summary>The authenticated principal of the current request, if any. Implemented by the host.</summary>
public interface ICurrentUser
{
    Guid? UserId { get; }

    Guid? SessionId { get; }
}
