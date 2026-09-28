namespace OpenCampus.Sis.Application.Abstractions;

/// <summary>
/// Issues the next learner number for a self-registered learner (Increment 7). Implemented in infrastructure over a
/// database sequence so concurrent registrations never collide; administrators still type numbers by hand on the
/// Learners screen, and the two ranges are kept apart by where the sequence starts.
/// </summary>
public interface ILearnerNumberGenerator
{
    Task<string> NextAsync(CancellationToken cancellationToken);
}
