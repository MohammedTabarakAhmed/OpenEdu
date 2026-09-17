using OpenCampus.Lms.Application.Abstractions;
using OpenCampus.Lms.Application.Content;
using OpenCampus.Lms.Domain.Attendance;
using OpenCampus.SharedKernel;

namespace OpenCampus.Lms.Application.Assessment;

/// <summary>
/// Attendance recording and retrieval (15.3). Sessions come from SIS through the contract; BR-13 is decided by the
/// aggregate from the session's scheduled start. After every register update each affected learner's rate for the
/// section is reported to SIS (6.5 "assessment outcomes"), where BR-12 is applied. Scope per section (SEC-12):
/// managers record and see everyone; a learner sees only their own records.
/// </summary>
public sealed class AttendanceService(
    IAttendanceRepository attendance,
    ISectionAccess sections,
    IAssessmentOutcomes outcomes,
    ICurrentUser currentUser,
    SectionScopeResolver scopes,
    ILmsUnitOfWork unitOfWork,
    TimeProvider clock)
{
    public async Task<Result<SectionAttendanceResponse>> GetForSectionAsync(Guid sectionId, CancellationToken cancellationToken)
    {
        var scope = await scopes.ResolveForReadingAsync(sectionId, cancellationToken);
        if (scope is null)
        {
            return Result.Failure<SectionAttendanceResponse>(LmsErrors.SectionNotFound);
        }

        var sessionList = await sections.ListSessionsAsync(sectionId, cancellationToken);
        var records = await attendance.ListBySessionsAsync(sessionList.Select(s => s.Id), cancellationToken);
        if (!scope.CanManage)
        {
            records = records.Where(r => r.LearnerUserId == currentUser.UserId).ToList();
        }

        var learnerIds = scope.CanManage
            ? (await sections.ListEnrolledLearnersAsync(sectionId, cancellationToken)).Select(l => l.UserId)
            : [currentUser.UserId!.Value];
        var summaries = learnerIds.Select(id => Summarise(id, sessionList, records)).ToList();

        return Result.Success(new SectionAttendanceResponse(scope.Section, scope.CanManage, sessionList, summaries, records.Select(r => r.ToResponse()).ToList()));
    }

    public async Task<Result<SessionRegisterResponse>> GetRegisterAsync(Guid sectionId, Guid sessionId, CancellationToken cancellationToken)
    {
        var (scope, session) = await LoadManagedSessionAsync(sectionId, sessionId, cancellationToken);
        if (scope is null || session is null)
        {
            return Result.Failure<SessionRegisterResponse>(LmsErrors.SessionNotFound);
        }

        var learners = await sections.ListEnrolledLearnersAsync(sectionId, cancellationToken);
        var records = await attendance.ListBySessionAsync(sessionId, cancellationToken);
        return Result.Success(new SessionRegisterResponse(session, learners, records.Select(r => r.ToResponse()).ToList()));
    }

    /// <summary>Records or amends the named learners' statuses for the session (BR-13 in the aggregate), then reports rates (BR-12).</summary>
    public async Task<Result<SessionRegisterResponse>> RecordAsync(Guid sectionId, Guid sessionId, RecordAttendanceRequest request, CancellationToken cancellationToken)
    {
        var (scope, session) = await LoadManagedSessionAsync(sectionId, sessionId, cancellationToken);
        if (scope is null || session is null)
        {
            return Result.Failure<SessionRegisterResponse>(LmsErrors.SessionNotFound);
        }

        var enrolled = (await sections.ListEnrolledLearnersAsync(sectionId, cancellationToken)).Select(l => l.UserId).ToHashSet();
        var unknown = request.Entries.FirstOrDefault(e => !enrolled.Contains(e.LearnerUserId));
        if (unknown is not null)
        {
            return Result.Failure<SessionRegisterResponse>(LmsErrors.LearnerNotEnrolled(unknown.LearnerUserId));
        }

        var actor = currentUser.UserId!.Value;
        var now = clock.GetUtcNow().UtcDateTime;
        foreach (var entry in request.Entries)
        {
            var existing = await attendance.FindAsync(sessionId, entry.LearnerUserId, cancellationToken);
            if (existing is null)
            {
                attendance.Add(AttendanceRecord.Create(sessionId, session.ScheduledStartUtc, entry.LearnerUserId, entry.Status, actor, now));
            }
            else
            {
                existing.Amend(entry.Status, actor, now);
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        // BR-12: the rate is the learner's attended sessions over the sessions for which a register exists.
        var sessionList = await sections.ListSessionsAsync(sectionId, cancellationToken);
        var records = await attendance.ListBySessionsAsync(sessionList.Select(s => s.Id), cancellationToken);
        foreach (var learnerUserId in request.Entries.Select(e => e.LearnerUserId).Distinct())
        {
            var summary = Summarise(learnerUserId, sessionList, records);
            await outcomes.ReportAttendanceRateAsync(sectionId, learnerUserId, summary.AttendancePercent, cancellationToken);
        }

        return await GetRegisterAsync(sectionId, sessionId, cancellationToken);
    }

    /// <summary>Sessions held = sessions with at least one record; attended = the learner's Present or Late records among them.</summary>
    public static LearnerAttendanceSummary Summarise(Guid learnerUserId, IReadOnlyList<SessionSummary> sessions, IReadOnlyList<AttendanceRecord> records)
    {
        var held = sessions.Where(s => records.Any(r => r.SessionId == s.Id)).Select(s => s.Id).ToHashSet();
        var attended = records.Count(r => r.LearnerUserId == learnerUserId && held.Contains(r.SessionId) && r.CountsAsAttended);
        var percent = held.Count == 0 ? 100m : Math.Round(attended * 100m / held.Count, 2, MidpointRounding.AwayFromZero);
        return new LearnerAttendanceSummary(learnerUserId, held.Count, attended, percent);
    }

    private async Task<(SectionScope? Scope, SessionSummary? Session)> LoadManagedSessionAsync(Guid sectionId, Guid sessionId, CancellationToken cancellationToken)
    {
        var scope = await scopes.ResolveForManagementAsync(sectionId, cancellationToken);
        if (scope is null)
        {
            return (null, null);
        }

        var session = (await sections.ListSessionsAsync(sectionId, cancellationToken)).SingleOrDefault(s => s.Id == sessionId);
        return (scope, session);
    }
}
