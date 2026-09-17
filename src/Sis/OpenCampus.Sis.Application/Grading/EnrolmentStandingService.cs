using Microsoft.Extensions.Options;
using OpenCampus.Sis.Application.Abstractions;
using OpenCampus.Sis.Application.External;
using OpenCampus.Sis.Domain.Enrolments;

namespace OpenCampus.Sis.Application.Grading;

/// <summary>
/// The SIS side of the LMS→SIS "assessment outcomes" contract (6.5): applies a reported attendance rate to the
/// learner's enrolment under BR-12 with the configured threshold (Appendix B "Academic"). Called by the host
/// adapter only; the LMS never touches enrolments directly (MB-01). A transition into At Risk is followed by an SMS
/// notice (EXT-02) once the change is committed.
/// </summary>
public sealed class EnrolmentStandingService(
    IEnrolmentRepository enrolments,
    ILearnerRepository learners,
    ISectionRepository sections,
    IOptions<AcademicOptions> academic,
    LearnerNotifier notifier,
    ISisUnitOfWork unitOfWork)
{
    public async Task ApplyAttendanceRateAsync(Guid sectionId, Guid learnerUserId, decimal attendancePercent, CancellationToken cancellationToken)
    {
        var enrolment = await enrolments.FindByUserAndSectionAsync(learnerUserId, sectionId, cancellationToken);
        if (enrolment is null || !enrolment.IsActive)
        {
            return;
        }

        var before = enrolment.Status;
        enrolment.ApplyAttendanceRate(attendancePercent, academic.Value.AttendanceThresholdPercent);
        if (enrolment.Status == before)
        {
            return;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (enrolment.Status == EnrolmentStatus.AtRisk)
        {
            var learner = await learners.FindByIdAsync(enrolment.LearnerId, cancellationToken);
            var section = await sections.FindByIdAsync(sectionId, cancellationToken);
            if (learner is not null && section is not null)
            {
                await notifier.PlacedAtRiskAsync(learner, section.Code, attendancePercent, cancellationToken);
            }
        }
    }
}
