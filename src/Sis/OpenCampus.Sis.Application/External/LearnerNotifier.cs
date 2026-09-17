using Microsoft.Extensions.Logging;
using OpenCampus.Sis.Application.Abstractions;
using OpenCampus.Sis.Domain.Learners;

namespace OpenCampus.Sis.Application.External;

/// <summary>
/// The SIS's use of EXT-01/EXT-02: learner-facing notices for grade release, certificate issuance and the BR-12
/// At-Risk transition. A notice is a side effect of a committed business change, never a condition of it: dispatch
/// outcomes are logged (partial and total failures at warning level) and never surface to the caller. Recipients are
/// resolved through the Identity contract (e-mail) and the learner record (telephone); a learner with no usable
/// address is simply not notified.
/// </summary>
public sealed class LearnerNotifier(IEmailDispatcher email, ISmsDispatcher sms, IUserDirectory users, ILogger<LearnerNotifier> logger)
{
    public async Task GradesReleasedAsync(IReadOnlyList<Learner> learners, string sectionCode, string courseCode, string courseNameEn, CancellationToken cancellationToken)
    {
        var addresses = await AddressesAsync(learners, cancellationToken);
        if (addresses.Count == 0)
        {
            return;
        }

        var message = new EmailMessage(
            addresses,
            $"Grades released: {courseCode} {sectionCode}",
            $"Your final grade for {courseCode} — {courseNameEn} (section {sectionCode}) has been released. Sign in to OpenCampus to view your results.");
        Report("grades released", sectionCode, await email.SendAsync(message, cancellationToken));
    }

    public async Task CertificateIssuedAsync(Learner learner, string courseCode, string courseNameEn, string verificationCode, CancellationToken cancellationToken)
    {
        var addresses = await AddressesAsync([learner], cancellationToken);
        if (addresses.Count == 0)
        {
            return;
        }

        var message = new EmailMessage(
            addresses,
            $"Your certificate for {courseCode}",
            $"Your certificate of completion for {courseCode} — {courseNameEn} has been issued. Verification code: {verificationCode}. Sign in to OpenCampus to download it.");
        Report("certificate issued", learner.LearnerNumber, await email.SendAsync(message, cancellationToken));
    }

    /// <summary>BR-12: the learner is told by SMS when attendance places the enrolment At Risk; no telephone, no message.</summary>
    public async Task PlacedAtRiskAsync(Learner learner, string sectionCode, decimal attendancePercent, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(learner.Phone))
        {
            return;
        }

        var message = new SmsMessage(
            [learner.Phone],
            $"OpenCampus: your attendance in {sectionCode} is {Math.Round(attendancePercent, 1)}% and your enrolment is now at risk. Please contact your instructor.");
        Report("at-risk notice", learner.LearnerNumber, await sms.SendAsync(message, cancellationToken));
    }

    private async Task<IReadOnlyList<string>> AddressesAsync(IReadOnlyList<Learner> learners, CancellationToken cancellationToken)
    {
        var usersById = await users.FindManyAsync(learners.Select(l => l.UserId), cancellationToken);
        return learners.Select(l => usersById.GetValueOrDefault(l.UserId)?.Email).Where(e => !string.IsNullOrWhiteSpace(e)).Cast<string>().Distinct().ToList();
    }

    private void Report(string notice, string subject, ExternalResult result)
    {
        switch (result.Outcome)
        {
            case ExternalOutcome.Succeeded:
                logger.LogInformation("Notice '{Notice}' for {Subject} dispatched ({Reference})", notice, subject, result.Reference);
                break;
            case ExternalOutcome.PartiallySucceeded:
                logger.LogWarning("Notice '{Notice}' for {Subject} partially dispatched ({Reference}); {Failed} target(s) refused", notice, subject, result.Reference, result.Failures.Count);
                break;
            default:
                logger.LogWarning("Notice '{Notice}' for {Subject} not dispatched: {Reasons}", notice, subject, string.Join("; ", result.Failures.Select(f => f.Reason)));
                break;
        }
    }
}
