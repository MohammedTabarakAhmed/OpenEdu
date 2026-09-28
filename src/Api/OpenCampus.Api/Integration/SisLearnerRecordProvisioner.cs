using OpenCampus.Identity.Application.Abstractions;
using OpenCampus.Sis.Application.Learners;

namespace OpenCampus.Api.Integration;

/// <summary>
/// Implements the Identity→SIS contract of Increment 7 (6.5, MB-02) at the composition root: a self-registered learner
/// whose account has just been activated gets a learner record with a generated number. A refusal from the SIS
/// (the account is not active or lacks the Learner role) is an invariant breach at this point and is surfaced as an
/// exception for the caller to log; the Identity change is already committed either way.
/// </summary>
public sealed class SisLearnerRecordProvisioner(LearnerService learners) : ILearnerRecordProvisioner
{
    public async Task EnsureAsync(Guid userId, CancellationToken cancellationToken)
    {
        var result = await learners.EnsureSelfRegisteredAsync(userId, cancellationToken);
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"Learner record was not provisioned: {result.Error.Code} — {result.Error.Message}");
        }
    }
}
