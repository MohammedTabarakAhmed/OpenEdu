using OpenCampus.SharedKernel;
using OpenCampus.Sis.Domain.Learners;
using OpenCampus.Sis.Domain.Sections;

namespace OpenCampus.Sis.Domain.Enrolments;

/// <summary>Enrolment statuses (reference data, SDD 13.6). Active and AtRisk both count as active enrolments.</summary>
public enum EnrolmentStatus
{
    Active = 1,
    AtRisk = 2,
    Withdrawn = 3,
    Completed = 4,
}

/// <summary>
/// Aggregate root for the association of a learner with a section (SDD 13.3, Appendix D).
/// Owns BR-02, BR-06 (grade visibility) and BR-12 (attendance → At Risk); delegates BR-01 and BR-03 to the
/// section at creation (11.3). Completion records the weighted final grade on release.
/// </summary>
public sealed class Enrolment : Entity
{
    private Enrolment()
    {
    }

    public Guid LearnerId { get; private set; }

    public Guid SectionId { get; private set; }

    public DateTime EnrolledAtUtc { get; private set; }

    public EnrolmentStatus Status { get; private set; }

    /// <summary>DC-05: exact decimal, precision 5 scale 2. Set on completion at grade release.</summary>
    public decimal? FinalGrade { get; private set; }

    public DateTime? CompletedAtUtc { get; private set; }

    public bool IsActive => Status is EnrolmentStatus.Active or EnrolmentStatus.AtRisk;

    /// <summary>
    /// Creates an enrolment after every section-14 precondition holds:
    /// BR-03 (section Open) and BR-01 (capacity) via the section; BR-02 (no second active enrolment) here.
    /// </summary>
    public static Enrolment Create(Learner learner, CourseSection section, int activeEnrolmentCount, bool learnerHasActiveEnrolmentInSection, DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(learner);
        ArgumentNullException.ThrowIfNull(section);

        if (!learner.CanEnrol)
        {
            throw new DomainException($"A {learner.Status} learner cannot enrol.");
        }

        if (learnerHasActiveEnrolmentInSection)
        {
            throw BusinessRules.Br02DuplicateEnrolment();
        }

        section.EnsureAcceptsEnrolment(activeEnrolmentCount);

        return new Enrolment
        {
            LearnerId = learner.Id,
            SectionId = section.Id,
            EnrolledAtUtc = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc),
            Status = EnrolmentStatus.Active,
        };
    }

    public void Withdraw()
    {
        if (!IsActive)
        {
            throw new DomainException($"A {Status} enrolment cannot be withdrawn.");
        }

        Status = EnrolmentStatus.Withdrawn;
    }

    /// <summary>
    /// Re-enrols a learner whose earlier enrolment in the section was withdrawn. The 13.3 unique index on
    /// (LearnerId, SectionId) permits one row per pair, so the existing row is reinstated under the same
    /// preconditions as creation: BR-02 (not already active), then BR-03 and BR-01 via the section.
    /// </summary>
    public void Reinstate(Learner learner, CourseSection section, int activeEnrolmentCount, DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(learner);
        ArgumentNullException.ThrowIfNull(section);

        if (learner.Id != LearnerId || section.Id != SectionId)
        {
            throw new DomainException("The enrolment belongs to a different learner or section.");
        }

        if (!learner.CanEnrol)
        {
            throw new DomainException($"A {learner.Status} learner cannot enrol.");
        }

        if (IsActive)
        {
            throw BusinessRules.Br02DuplicateEnrolment();
        }

        if (Status == EnrolmentStatus.Completed)
        {
            throw new DomainException("A completed enrolment cannot be reinstated.");
        }

        section.EnsureAcceptsEnrolment(activeEnrolmentCount);

        Status = EnrolmentStatus.Active;
        EnrolledAtUtc = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
    }

    // ----- BR-12: attendance below the configured threshold places the enrolment At Risk -----

    /// <summary>
    /// Applies the learner's attendance rate for the section. Below the threshold an Active enrolment becomes
    /// At Risk; at or above it an At Risk enrolment returns to Active. Withdrawn and Completed are unaffected.
    /// </summary>
    public void ApplyAttendanceRate(decimal attendancePercent, decimal thresholdPercent)
    {
        if (attendancePercent is < 0 or > 100 || thresholdPercent is < 0 or > 100)
        {
            throw new DomainException("Attendance and threshold percentages must be between 0 and 100.");
        }

        if (!IsActive)
        {
            return;
        }

        Status = attendancePercent < thresholdPercent ? EnrolmentStatus.AtRisk : EnrolmentStatus.Active;
    }

    // ----- BR-06: grade entries are not visible to a learner until released -----

    /// <summary>The learner's view of their grade entries: released ones only.</summary>
    public IEnumerable<GradeEntry> GradesVisibleToLearner(IEnumerable<GradeEntry> entries) =>
        entries.Where(e => e.EnrolmentId == Id && e.IsReleased);

    /// <summary>BR-06 as a refusal for a direct request.</summary>
    public void EnsureGradeVisibleToLearner(GradeEntry entry)
    {
        if (entry.EnrolmentId != Id)
        {
            throw new DomainException("The grade entry belongs to a different enrolment.");
        }

        if (!entry.IsReleased)
        {
            throw BusinessRules.Br06GradesNotReleased();
        }
    }

    // ----- Completion at release -----

    /// <summary>Records the weighted final grade and completes the enrolment; only an active enrolment can complete.</summary>
    public void Complete(decimal finalGrade, DateTime utcNow)
    {
        if (!IsActive)
        {
            throw new DomainException($"A {Status} enrolment cannot be completed.");
        }

        if (finalGrade is < 0 or > 100)
        {
            throw new DomainException("The final grade must be between 0 and 100.");
        }

        FinalGrade = finalGrade;
        CompletedAtUtc = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
        Status = EnrolmentStatus.Completed;
    }
}

/// <summary>
/// A score against one grade component of an enrolment (SDD 13.3; unique on (EnrolmentId, GradeComponentId)).
/// Owns BR-05: a score is neither negative nor greater than the component maximum. Released entries are final.
/// </summary>
public sealed class GradeEntry : Entity
{
    private GradeEntry()
    {
    }

    public Guid EnrolmentId { get; private set; }

    public Guid GradeComponentId { get; private set; }

    /// <summary>DC-05: exact decimal, precision 7 scale 2.</summary>
    public decimal Score { get; private set; }

    public bool IsReleased { get; private set; }

    /// <summary>Cross-module reference to the marking user (MB-03).</summary>
    public Guid GradedByUserId { get; private set; }

    public DateTime GradedAtUtc { get; private set; }

    /// <summary>Creates a grade for an active enrolment against one component of its section (BR-05).</summary>
    public static GradeEntry Create(Enrolment enrolment, GradeComponent component, decimal score, Guid gradedByUserId, DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(enrolment);
        ArgumentNullException.ThrowIfNull(component);

        if (!enrolment.IsActive)
        {
            throw new DomainException($"A {enrolment.Status} enrolment cannot be graded.");
        }

        if (component.SectionId != enrolment.SectionId)
        {
            throw new DomainException("The grade component belongs to a different section.");
        }

        var entry = new GradeEntry { EnrolmentId = enrolment.Id, GradeComponentId = component.Id };
        entry.Amend(component, score, gradedByUserId, utcNow);
        return entry;
    }

    public void Amend(GradeComponent component, decimal score, Guid gradedByUserId, DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(component);

        if (component.Id != GradeComponentId)
        {
            throw new DomainException("The grade component does not match this entry.");
        }

        if (IsReleased)
        {
            throw new DomainException("A released grade entry cannot be amended.");
        }

        if (score < 0 || score > component.MaxScore)
        {
            throw BusinessRules.Br05ScoreOutOfRange(component.MaxScore);
        }

        Score = score;
        GradedByUserId = Guard.RequireId(gradedByUserId, nameof(gradedByUserId));
        GradedAtUtc = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
    }

    /// <summary>Makes the entry visible to the learner (BR-06); the section decides when all entries may be released (BR-07).</summary>
    public void Release()
    {
        IsReleased = true;
    }
}

/// <summary>
/// The certificate issued for a completed enrolment (SDD 13.3: Enrolment 1—1 Certificate). The
/// schema is delivered in Increment 3; issuance and BR-10/BR-11 are delivered in Increment 6.
/// </summary>
public sealed class Certificate : Entity
{
    public const int VerificationCodeMaxLength = 32;
    public const int FilePathMaxLength = 400;

    private Certificate()
    {
    }

    public Guid EnrolmentId { get; private set; }

    public string VerificationCode { get; private set; } = null!;

    public DateTime IssuedAtUtc { get; private set; }

    /// <summary>Relative path only (18.5); content is never stored in the database.</summary>
    public string FilePath { get; private set; } = null!;
}
