using OpenCampus.SharedKernel;
using OpenCampus.Sis.Domain.Courses;
using OpenCampus.Sis.Domain.Enrolments;
using OpenCampus.Sis.Domain.Learners;
using OpenCampus.Sis.Domain.Programmes;
using OpenCampus.Sis.Domain.Sections;

namespace OpenCampus.Sis.Application.Abstractions;

/// <summary>Persistence contracts declared by the application layer and implemented in infrastructure (LR-03).</summary>
public interface IProgrammeRepository
{
    Task<Programme?> FindByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, Programme>> FindManyAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken);

    Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken);

    Task<int> CountCoursesAsync(Guid programmeId, CancellationToken cancellationToken);

    Task<PagedResponse<Programme>> ListAsync(ProgrammeQuery query, CancellationToken cancellationToken);

    void Add(Programme programme);
}

public sealed record ProgrammeQuery(int Page, int PageSize, string? Search, bool? IsActive, string? Sort, bool Descending);

public interface ICourseRepository
{
    Task<Course?> FindByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, Course>> FindManyAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken);

    Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken);

    Task<int> CountSectionsAsync(Guid courseId, CancellationToken cancellationToken);

    Task<PagedResponse<Course>> ListAsync(CourseQuery query, CancellationToken cancellationToken);

    void Add(Course course);
}

public sealed record CourseQuery(int Page, int PageSize, string? Search, Guid? ProgrammeId, string? Sort, bool Descending);

public interface ISectionRepository
{
    /// <summary>Loads the aggregate with its sessions and grade components.</summary>
    Task<CourseSection?> FindByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, CourseSection>> FindManyAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken);

    Task<bool> CodeExistsAsync(Guid courseId, string code, CancellationToken cancellationToken);

    /// <summary>Sessions of every other section assigned to the instructor, for BR-16.</summary>
    Task<IReadOnlyList<Session>> GetInstructorSessionsElsewhereAsync(Guid instructorUserId, Guid excludingSectionId, CancellationToken cancellationToken);

    Task<PagedResponse<CourseSection>> ListAsync(SectionQuery query, CancellationToken cancellationToken);

    void Add(CourseSection section);
}

public sealed record SectionQuery(
    int Page, int PageSize, string? Search, Guid? CourseId, Guid? ProgrammeId, SectionStatus? Status, DeliveryMode? DeliveryMode, Guid? InstructorUserId, string? Sort, bool Descending);

public interface ILearnerRepository
{
    Task<Learner?> FindByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<Learner?> FindByUserIdAsync(Guid userId, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, Learner>> FindManyAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken);

    Task<bool> LearnerNumberExistsAsync(string learnerNumber, CancellationToken cancellationToken);

    Task<bool> UserIdExistsAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>The subset of the given user identifiers that already have a learner record (one query, NFR-04).</summary>
    Task<IReadOnlySet<Guid>> GetLinkedUserIdsAsync(IEnumerable<Guid> userIds, CancellationToken cancellationToken);

    Task<PagedResponse<Learner>> ListAsync(LearnerQuery query, CancellationToken cancellationToken);

    void Add(Learner learner);
}

/// <summary>Search matches the learner number, or the user identifiers resolved from a name search through the Identity contract.</summary>
public sealed record LearnerQuery(int Page, int PageSize, string? Search, IReadOnlyCollection<Guid>? MatchingUserIds, LearnerStatus? Status, string? Sort, bool Descending);

public interface IEnrolmentRepository
{
    Task<Enrolment?> FindByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<int> CountActiveInSectionAsync(Guid sectionId, CancellationToken cancellationToken);

    /// <summary>Active enrolment counts for many sections in one query (NFR-04).</summary>
    Task<IReadOnlyDictionary<Guid, int>> CountActiveInSectionsAsync(IEnumerable<Guid> sectionIds, CancellationToken cancellationToken);

    Task<int> CountInSectionAsync(Guid sectionId, CancellationToken cancellationToken);

    /// <summary>The single enrolment row for the pair, whatever its status (13.3 unique index).</summary>
    Task<Enrolment?> FindByLearnerAndSectionAsync(Guid learnerId, Guid sectionId, CancellationToken cancellationToken);

    Task<PagedResponse<Enrolment>> ListAsync(EnrolmentQuery query, CancellationToken cancellationToken);

    /// <summary>Every active (Active or At Risk) enrolment of a section, for grading and release. Bounded by the section capacity.</summary>
    Task<IReadOnlyList<Enrolment>> ListActiveInSectionAsync(Guid sectionId, CancellationToken cancellationToken);

    /// <summary>Every enrolment of a section that belongs in its gradebook: active ones and completed ones, not withdrawn.</summary>
    Task<IReadOnlyList<Enrolment>> ListGradableInSectionAsync(Guid sectionId, CancellationToken cancellationToken);

    /// <summary>The enrolment, whatever its status, of the learner linked to the given user in the section — the cross-module lookup the LMS needs.</summary>
    Task<Enrolment?> FindByUserAndSectionAsync(Guid userId, Guid sectionId, CancellationToken cancellationToken);

    void Add(Enrolment enrolment);
}

public interface IGradeEntryRepository
{
    Task<GradeEntry?> FindByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<GradeEntry?> FindByEnrolmentAndComponentAsync(Guid enrolmentId, Guid gradeComponentId, CancellationToken cancellationToken);

    Task<IReadOnlyList<GradeEntry>> ListByEnrolmentAsync(Guid enrolmentId, CancellationToken cancellationToken);

    /// <summary>All entries against the section's enrolments in one query (gradebook, BR-07, release).</summary>
    Task<IReadOnlyList<GradeEntry>> ListBySectionAsync(Guid sectionId, CancellationToken cancellationToken);

    void Add(GradeEntry entry);
}

public sealed record EnrolmentQuery(int Page, int PageSize, Guid? LearnerId, Guid? SectionId, EnrolmentStatus? Status, bool? ActiveOnly);

/// <summary>Commits all pending changes of the SIS module atomically.</summary>
public interface ISisUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
