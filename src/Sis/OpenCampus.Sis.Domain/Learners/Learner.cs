using OpenCampus.SharedKernel;

namespace OpenCampus.Sis.Domain.Learners;

public enum Gender
{
    Unspecified = 0,
    Male = 1,
    Female = 2,
}

public enum LearnerStatus
{
    Active = 1,
    Suspended = 2,
    Graduated = 3,
    Withdrawn = 4,
}

/// <summary>
/// Aggregate root for a learner record (SDD 13.3). UserId is a cross-module reference to the
/// Identity account held as an identifier only (MB-03); it is validated through the Identity
/// contract at creation (13.5).
/// </summary>
public sealed class Learner : Entity
{
    public const int LearnerNumberMaxLength = 20;
    public const int NationalIdMaxLength = 30;
    public const int PhoneMaxLength = 30;

    private Learner()
    {
    }

    public Guid UserId { get; private set; }

    public string LearnerNumber { get; private set; } = null!;

    public string? NationalId { get; private set; }

    public DateOnly? DateOfBirth { get; private set; }

    public Gender Gender { get; private set; }

    public string? Phone { get; private set; }

    public LearnerStatus Status { get; private set; }

    public bool CanEnrol => Status == LearnerStatus.Active;

    public static Learner Create(Guid userId, string learnerNumber, string? nationalId, DateOnly? dateOfBirth, Gender gender, string? phone)
    {
        var learner = new Learner
        {
            UserId = Guard.RequireId(userId, nameof(userId)),
            LearnerNumber = Guard.RequireText(learnerNumber, nameof(learnerNumber), LearnerNumberMaxLength).ToUpperInvariant(),
            Status = LearnerStatus.Active,
        };
        learner.Amend(nationalId, dateOfBirth, gender, phone);
        return learner;
    }

    public void Amend(string? nationalId, DateOnly? dateOfBirth, Gender gender, string? phone)
    {
        if (!Enum.IsDefined(gender))
        {
            throw new DomainException("gender is not a recognised value.");
        }

        NationalId = Guard.OptionalText(nationalId, nameof(nationalId), NationalIdMaxLength);
        DateOfBirth = dateOfBirth;
        Gender = gender;
        Phone = Guard.OptionalText(phone, nameof(phone), PhoneMaxLength);
    }

    public void ChangeStatus(LearnerStatus status)
    {
        if (!Enum.IsDefined(status))
        {
            throw new DomainException("status is not a recognised learner status.");
        }

        Status = status;
    }
}
