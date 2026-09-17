using OpenCampus.SharedKernel;

namespace OpenCampus.Lms.Domain.Attendance;

/// <summary>Attendance statuses (reference data, SDD 13.6).</summary>
public enum AttendanceStatus
{
    Present = 1,
    Absent = 2,
    Late = 3,
    Excused = 4,
}

/// <summary>
/// A learner's attendance at one scheduled session (SDD 13.4; unique on (SessionId, LearnerUserId)). Owns BR-13:
/// no record for a session scheduled in the future. SessionId, LearnerUserId and RecordedByUserId are
/// cross-module references (MB-03); the session's scheduled start is supplied by the caller from the SIS contract.
/// </summary>
public sealed class AttendanceRecord : Entity
{
    private AttendanceRecord()
    {
    }

    public Guid SessionId { get; private set; }

    public Guid LearnerUserId { get; private set; }

    public AttendanceStatus Status { get; private set; }

    public Guid RecordedByUserId { get; private set; }

    public DateTime RecordedAtUtc { get; private set; }

    /// <summary>Present and Late count as attended for the BR-12 attendance rate; Absent and Excused do not.</summary>
    public bool CountsAsAttended => Status is AttendanceStatus.Present or AttendanceStatus.Late;

    public static AttendanceRecord Create(Guid sessionId, DateTime sessionScheduledStartUtc, Guid learnerUserId, AttendanceStatus status, Guid recordedByUserId, DateTime utcNow)
    {
        if (sessionScheduledStartUtc > utcNow)
        {
            throw BusinessRules.Br13SessionInFuture();
        }

        var record = new AttendanceRecord
        {
            SessionId = Guard.RequireId(sessionId, nameof(sessionId)),
            LearnerUserId = Guard.RequireId(learnerUserId, nameof(learnerUserId)),
        };
        record.Amend(status, recordedByUserId, utcNow);
        return record;
    }

    public void Amend(AttendanceStatus status, Guid recordedByUserId, DateTime utcNow)
    {
        if (!Enum.IsDefined(status))
        {
            throw new DomainException("status is not a recognised attendance status.");
        }

        Status = status;
        RecordedByUserId = Guard.RequireId(recordedByUserId, nameof(recordedByUserId));
        RecordedAtUtc = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
    }
}
