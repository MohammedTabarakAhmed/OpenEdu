namespace OpenCampus.Identity.Application.Abstractions;

/// <summary>
/// Identity→SIS contract (6.5, MB-02): once a self-registered learner is activated, the SIS creates the learner record
/// with a generated learner number. Implemented by the host adapter. Must be idempotent — a second call for a user who
/// already has a record does nothing — because it runs after the Identity commit and may be retried.
/// </summary>
public interface ILearnerRecordProvisioner
{
    Task EnsureAsync(Guid userId, CancellationToken cancellationToken);
}
