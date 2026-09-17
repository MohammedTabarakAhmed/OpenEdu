namespace OpenCampus.Lms.Application.Abstractions;

/// <summary>A SIS section as the LMS needs to see it: identity, display names and the assigned instructor.</summary>
public sealed record SectionSummary(
    Guid Id,
    string Code,
    string TermName,
    string Status,
    Guid CourseId,
    string CourseCode,
    string CourseNameEn,
    string CourseNameAr,
    Guid InstructorUserId);

/// <summary>A scheduled session of a section as the LMS needs it for attendance (BR-13).</summary>
public sealed record SessionSummary(Guid Id, Guid SectionId, DateTime ScheduledStartUtc, DateTime ScheduledEndUtc, string? Location);

/// <summary>An actively enrolled learner of a section, by user identifier, with display names resolved by SIS.</summary>
public sealed record EnrolledLearner(Guid UserId, string LearnerNumber, string FullNameEn, string FullNameAr);

/// <summary>
/// Cross-module contract to the SIS module (6.5: "confirm a learner's enrolment in a section; retrieve section
/// and instructor assignment"). Declared in the consuming module's application layer (MB-02) and implemented
/// in the host composition root over SIS (MB-05). It is the sole source of the facts SEC-12 needs: whether the
/// caller is the assigned instructor and whether the caller holds an active enrolment.
/// </summary>
public interface ISectionAccess
{
    Task<SectionSummary?> FindSectionAsync(Guid sectionId, CancellationToken cancellationToken);

    /// <summary>True when the user's learner record holds an Active or At Risk enrolment in the section.</summary>
    Task<bool> IsLearnerEnrolledAsync(Guid userId, Guid sectionId, CancellationToken cancellationToken);

    /// <summary>Sections assigned to the instructor, bounded to live (Draft, Open) and Closed sections, ordered by start date.</summary>
    Task<IReadOnlyList<SectionSummary>> ListSectionsForInstructorAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Sections in which the user's learner record holds an active enrolment.</summary>
    Task<IReadOnlyList<SectionSummary>> ListSectionsForLearnerAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>All Draft and Open sections (bounded), for demonstration-data provisioning only.</summary>
    Task<IReadOnlyList<SectionSummary>> ListLiveSectionsAsync(CancellationToken cancellationToken);

    /// <summary>The section's scheduled sessions, ordered by start; attendance is recorded against these (BR-13).</summary>
    Task<IReadOnlyList<SessionSummary>> ListSessionsAsync(Guid sectionId, CancellationToken cancellationToken);

    /// <summary>The actively enrolled learners of a section, for registers and marking views.</summary>
    Task<IReadOnlyList<EnrolledLearner>> ListEnrolledLearnersAsync(Guid sectionId, CancellationToken cancellationToken);
}

/// <summary>
/// Cross-module contract to the SIS module (6.5: "notify that assessment outcomes are available for grade
/// aggregation"). The LMS reports a learner's attendance rate for a section; SIS applies BR-12 against its
/// configured threshold. Declared here (MB-02), implemented in the host over SIS (MB-05).
/// </summary>
public interface IAssessmentOutcomes
{
    Task ReportAttendanceRateAsync(Guid sectionId, Guid learnerUserId, decimal attendancePercent, CancellationToken cancellationToken);
}
