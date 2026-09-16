using OpenCampus.SharedKernel;

namespace OpenCampus.Sis.Domain.Sections;

/// <summary>
/// Aggregate root for a scheduled delivery instance of a course (SDD 13.3, Appendix D). Owns its
/// sessions and grade components, and enforces BR-01, BR-03, BR-14 and BR-16 (section 14).
/// InstructorUserId is a cross-module reference held as an identifier only (MB-03).
/// </summary>
public sealed class CourseSection : Entity
{
    public const int CodeMaxLength = 30;
    public const int TermNameMaxLength = 50;
    public const int MaxCapacity = 1000;

    private readonly List<Session> _sessions = [];
    private readonly List<GradeComponent> _gradeComponents = [];

    private CourseSection()
    {
    }

    public Guid CourseId { get; private set; }

    public string Code { get; private set; } = null!;

    public string TermName { get; private set; } = null!;

    public DateOnly StartDate { get; private set; }

    public DateOnly EndDate { get; private set; }

    public int Capacity { get; private set; }

    public Guid InstructorUserId { get; private set; }

    public DeliveryMode DeliveryMode { get; private set; }

    public SectionStatus Status { get; private set; }

    public IReadOnlyCollection<Session> Sessions => _sessions.AsReadOnly();

    public IReadOnlyCollection<GradeComponent> GradeComponents => _gradeComponents.AsReadOnly();

    public decimal TotalWeightPercent => _gradeComponents.Sum(c => c.WeightPercent);

    public static CourseSection Create(
        Guid courseId, string code, string termName, DateOnly startDate, DateOnly endDate,
        int capacity, Guid instructorUserId, DeliveryMode deliveryMode)
    {
        var section = new CourseSection
        {
            CourseId = Guard.RequireId(courseId, nameof(courseId)),
            Code = Guard.RequireText(code, nameof(code), CodeMaxLength).ToUpperInvariant(),
            Status = SectionStatus.Draft,
        };
        section.Amend(termName, startDate, endDate, capacity, instructorUserId, deliveryMode, activeEnrolmentCount: 0);
        return section;
    }

    /// <summary>Capacity may not be reduced below the active enrolment count, or BR-01 could no longer hold.</summary>
    public void Amend(string termName, DateOnly startDate, DateOnly endDate, int capacity, Guid instructorUserId, DeliveryMode deliveryMode, int activeEnrolmentCount)
    {
        if (Status is SectionStatus.Closed or SectionStatus.Cancelled)
        {
            throw new DomainException($"A {Status} section cannot be amended.");
        }

        if (endDate < startDate)
        {
            throw new DomainException("endDate must not precede startDate.");
        }

        if (capacity < 1 || capacity > MaxCapacity)
        {
            throw new DomainException($"capacity must be between 1 and {MaxCapacity}.");
        }

        if (capacity < activeEnrolmentCount)
        {
            throw new DomainException($"capacity cannot be reduced below the {activeEnrolmentCount} active enrolments.");
        }

        if (!Enum.IsDefined(deliveryMode))
        {
            throw new DomainException("deliveryMode is not a recognised delivery mode.");
        }

        TermName = Guard.RequireText(termName, nameof(termName), TermNameMaxLength);
        StartDate = startDate;
        EndDate = endDate;
        Capacity = capacity;
        InstructorUserId = Guard.RequireId(instructorUserId, nameof(instructorUserId));
        DeliveryMode = deliveryMode;
    }

    // ----- State transitions (15.3 "section state transition") -----

    public void Open()
    {
        if (Status != SectionStatus.Draft)
        {
            throw new DomainException($"Only a Draft section can be opened; this section is {Status}.");
        }

        Status = SectionStatus.Open;
    }

    public void Close()
    {
        if (Status != SectionStatus.Open)
        {
            throw new DomainException($"Only an Open section can be closed; this section is {Status}.");
        }

        Status = SectionStatus.Closed;
    }

    public void Cancel()
    {
        if (Status is SectionStatus.Closed or SectionStatus.Cancelled)
        {
            throw new DomainException($"A {Status} section cannot be cancelled.");
        }

        Status = SectionStatus.Cancelled;
    }

    // ----- Enrolment invariants owned by the section -----

    /// <summary>BR-03 then BR-01: the section must be Open and must have a free place.</summary>
    public void EnsureAcceptsEnrolment(int activeEnrolmentCount)
    {
        if (Status != SectionStatus.Open)
        {
            throw BusinessRules.Br03SectionNotOpen();
        }

        if (activeEnrolmentCount >= Capacity)
        {
            throw BusinessRules.Br01SectionFull();
        }
    }

    /// <summary>BR-14: deletion is refused while any enrolment (active or not) exists.</summary>
    public void EnsureDeletable(int enrolmentCount)
    {
        if (enrolmentCount > 0)
        {
            throw BusinessRules.Br14SectionHasEnrolments();
        }
    }

    // ----- Scheduled sessions (15.3 "scheduled session management") -----

    /// <summary>
    /// BR-16: the new session may not overlap any session of the same instructor. The caller supplies
    /// the instructor's sessions from other sections; this section's own sessions are checked here.
    /// </summary>
    public Session AddSession(DateTime scheduledStartUtc, DateTime scheduledEndUtc, string? location, IEnumerable<Session> instructorSessionsElsewhere)
    {
        var session = Session.Create(Id, scheduledStartUtc, scheduledEndUtc, location);
        EnsureNoInstructorOverlap(session, instructorSessionsElsewhere);
        _sessions.Add(session);
        return session;
    }

    public Session RescheduleSession(Guid sessionId, DateTime scheduledStartUtc, DateTime scheduledEndUtc, string? location, IEnumerable<Session> instructorSessionsElsewhere)
    {
        var session = FindSession(sessionId);
        var candidate = Session.Create(Id, scheduledStartUtc, scheduledEndUtc, location);
        EnsureNoInstructorOverlap(candidate, instructorSessionsElsewhere, excludingSessionId: sessionId);
        session.Reschedule(scheduledStartUtc, scheduledEndUtc, location);
        return session;
    }

    public void RemoveSession(Guid sessionId)
    {
        _sessions.Remove(FindSession(sessionId));
    }

    private void EnsureNoInstructorOverlap(Session candidate, IEnumerable<Session> instructorSessionsElsewhere, Guid? excludingSessionId = null)
    {
        var own = _sessions.Where(s => s.Id != excludingSessionId);
        if (own.Concat(instructorSessionsElsewhere).Any(s => s.Overlaps(candidate.ScheduledStartUtc, candidate.ScheduledEndUtc)))
        {
            throw BusinessRules.Br16SessionOverlap();
        }
    }

    private Session FindSession(Guid sessionId) =>
        _sessions.SingleOrDefault(s => s.Id == sessionId) ?? throw new EntityNotFoundException(nameof(Session), sessionId);

    // ----- Grade scheme (15.3 "grade scheme definition"); BR-04 is enforced on Open in Increment 5 -----

    public GradeComponent AddGradeComponent(string nameEn, string nameAr, decimal weightPercent, decimal maxScore)
    {
        EnsureSchemeAmendable();
        var component = GradeComponent.Create(Id, nameEn, nameAr, weightPercent, maxScore);
        EnsureWeightsWithinBounds(TotalWeightPercent + weightPercent);
        _gradeComponents.Add(component);
        return component;
    }

    public GradeComponent AmendGradeComponent(Guid componentId, string nameEn, string nameAr, decimal weightPercent, decimal maxScore)
    {
        EnsureSchemeAmendable();
        var component = FindGradeComponent(componentId);
        EnsureWeightsWithinBounds(_gradeComponents.Where(c => c.Id != componentId).Sum(c => c.WeightPercent) + weightPercent);
        component.Amend(nameEn, nameAr, weightPercent, maxScore);
        return component;
    }

    public void RemoveGradeComponent(Guid componentId)
    {
        EnsureSchemeAmendable();
        _gradeComponents.Remove(FindGradeComponent(componentId));
    }

    private void EnsureSchemeAmendable()
    {
        if (Status is SectionStatus.Closed or SectionStatus.Cancelled)
        {
            throw new DomainException($"The grade scheme of a {Status} section cannot be amended.");
        }
    }

    private static void EnsureWeightsWithinBounds(decimal total)
    {
        if (total > 100)
        {
            throw new DomainException("The grade component weightings would exceed 100 percent.");
        }
    }

    private GradeComponent FindGradeComponent(Guid componentId) =>
        _gradeComponents.SingleOrDefault(c => c.Id == componentId) ?? throw new EntityNotFoundException(nameof(GradeComponent), componentId);
}
