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
/// Owns BR-02; delegates BR-01 and BR-03 to the section at creation (11.3).
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

    /// <summary>DC-05: exact decimal, precision 5 scale 2. Set on completion (Increment 5).</summary>
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
}

/// <summary>
/// A score against one grade component of an enrolment (SDD 13.3). The schema is delivered in
/// Increment 3; grading behaviour and BR-05 to BR-07 are delivered in Increment 5.
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
