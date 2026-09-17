using Microsoft.EntityFrameworkCore;
using OpenCampus.Lms.Application.Abstractions;
using OpenCampus.Lms.Domain.Assessment;
using OpenCampus.Lms.Domain.Attendance;

namespace OpenCampus.Lms.Infrastructure.Persistence.Repositories;

internal sealed class AssignmentRepository(LmsDbContext db) : IAssignmentRepository
{
    private IQueryable<Assignment> Aggregates => db.Assignments.Include(a => a.Submissions);

    public Task<Assignment?> FindByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Aggregates.SingleOrDefaultAsync(a => a.Id == id, cancellationToken);

    public async Task<Assignment?> FindBySubmissionIdAsync(Guid submissionId, CancellationToken cancellationToken)
    {
        var assignmentId = await db.Submissions.Where(s => s.Id == submissionId).Select(s => s.AssignmentId).SingleOrDefaultAsync(cancellationToken);
        return assignmentId == Guid.Empty ? null : await FindByIdAsync(assignmentId, cancellationToken);
    }

    public async Task<IReadOnlyList<Assignment>> ListBySectionAsync(Guid sectionId, CancellationToken cancellationToken) =>
        await Aggregates.Where(a => a.SectionId == sectionId).OrderBy(a => a.DueAtUtc).ThenBy(a => a.TitleEn).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Assignment>> ListPublishedBySectionsAsync(IEnumerable<Guid> sectionIds, CancellationToken cancellationToken)
    {
        var set = sectionIds.Distinct().ToArray();
        return await db.Assignments
            .Where(a => set.Contains(a.SectionId) && a.IsPublished)
            .OrderBy(a => a.DueAtUtc)
            .ToListAsync(cancellationToken);
    }

    public void Add(Assignment assignment) => db.Assignments.Add(assignment);

    /// <summary>DC-03: logical deletion of the assignment and its submissions.</summary>
    public void Remove(Assignment assignment)
    {
        foreach (var submission in assignment.Submissions)
        {
            submission.MarkDeleted();
        }

        assignment.MarkDeleted();
    }
}

internal sealed class AttendanceRepository(LmsDbContext db) : IAttendanceRepository
{
    public Task<AttendanceRecord?> FindAsync(Guid sessionId, Guid learnerUserId, CancellationToken cancellationToken) =>
        db.AttendanceRecords.SingleOrDefaultAsync(r => r.SessionId == sessionId && r.LearnerUserId == learnerUserId, cancellationToken);

    public async Task<IReadOnlyList<AttendanceRecord>> ListBySessionAsync(Guid sessionId, CancellationToken cancellationToken) =>
        await db.AttendanceRecords.Where(r => r.SessionId == sessionId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<AttendanceRecord>> ListBySessionsAsync(IEnumerable<Guid> sessionIds, CancellationToken cancellationToken)
    {
        var set = sessionIds.Distinct().ToArray();
        return await db.AttendanceRecords.Where(r => set.Contains(r.SessionId)).ToListAsync(cancellationToken);
    }

    public void Add(AttendanceRecord record) => db.AttendanceRecords.Add(record);
}
