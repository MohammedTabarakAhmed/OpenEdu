using Microsoft.Extensions.Options;
using OpenCampus.Sis.Application.Abstractions;

namespace OpenCampus.Sis.Application.Grading;

/// <summary>
/// The SIS side of the LMS→SIS "assessment outcomes" contract (6.5): applies a reported attendance rate to the
/// learner's enrolment under BR-12 with the configured threshold (Appendix B "Academic"). Called by the host
/// adapter only; the LMS never touches enrolments directly (MB-01).
/// </summary>
public sealed class EnrolmentStandingService(IEnrolmentRepository enrolments, IOptions<AcademicOptions> academic, ISisUnitOfWork unitOfWork)
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
        if (enrolment.Status != before)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }
}
