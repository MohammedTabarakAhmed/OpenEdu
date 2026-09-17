using OpenCampus.Lms.Domain.Assessment;
using OpenCampus.Lms.Domain.Attendance;
using OpenCampus.Lms.Domain.Content;

namespace OpenCampus.Lms.Application.Abstractions;

/// <summary>Persistence contracts declared by the application layer and implemented in infrastructure (LR-03).</summary>
public interface ICourseContentRepository
{
    /// <summary>Loads the aggregate with its items and their resources.</summary>
    Task<CourseContent?> FindByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The aggregate owning the given resource, fully loaded, or null.</summary>
    Task<CourseContent?> FindByResourceIdAsync(Guid resourceId, CancellationToken cancellationToken);

    /// <summary>Every content unit of a section with items and resources, ordered by sort order. Bounded by the size of one section's hierarchy (NFR-03).</summary>
    Task<IReadOnlyList<CourseContent>> ListBySectionAsync(Guid sectionId, CancellationToken cancellationToken);

    /// <summary>Whether any content exists at all (provisioning idempotence).</summary>
    Task<bool> AnyAsync(CancellationToken cancellationToken);

    void Add(CourseContent content);

    void Remove(CourseContent content);
}

public interface IAssignmentRepository
{
    /// <summary>Loads the aggregate with its submissions.</summary>
    Task<Assignment?> FindByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>The aggregate owning the given submission, fully loaded, or null.</summary>
    Task<Assignment?> FindBySubmissionIdAsync(Guid submissionId, CancellationToken cancellationToken);

    /// <summary>Every assignment of a section with submissions, ordered by due date. Bounded by one section's assignments.</summary>
    Task<IReadOnlyList<Assignment>> ListBySectionAsync(Guid sectionId, CancellationToken cancellationToken);

    /// <summary>Published assignments of many sections, without submissions, for a learner's overview (NFR-04).</summary>
    Task<IReadOnlyList<Assignment>> ListPublishedBySectionsAsync(IEnumerable<Guid> sectionIds, CancellationToken cancellationToken);

    void Add(Assignment assignment);

    void Remove(Assignment assignment);
}

public interface IAttendanceRepository
{
    Task<AttendanceRecord?> FindAsync(Guid sessionId, Guid learnerUserId, CancellationToken cancellationToken);

    Task<IReadOnlyList<AttendanceRecord>> ListBySessionAsync(Guid sessionId, CancellationToken cancellationToken);

    /// <summary>Records of many sessions in one query, for a section's register and the BR-12 rate.</summary>
    Task<IReadOnlyList<AttendanceRecord>> ListBySessionsAsync(IEnumerable<Guid> sessionIds, CancellationToken cancellationToken);

    void Add(AttendanceRecord record);
}

/// <summary>Commits all pending changes of the LMS module atomically.</summary>
public interface ILmsUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
