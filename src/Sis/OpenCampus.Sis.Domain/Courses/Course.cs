using OpenCampus.SharedKernel;

namespace OpenCampus.Sis.Domain.Courses;

/// <summary>Aggregate root for a course within a programme (SDD 13.3: Programme 1—* Course).</summary>
public sealed class Course : Entity
{
    public const int CodeMaxLength = 20;
    public const int NameMaxLength = 200;
    public const int DescriptionMaxLength = 4000;
    public const int MaxCredits = 60;

    private Course()
    {
    }

    public Guid ProgrammeId { get; private set; }

    public string Code { get; private set; } = null!;

    public string NameEn { get; private set; } = null!;

    public string NameAr { get; private set; } = null!;

    public string? DescriptionEn { get; private set; }

    public string? DescriptionAr { get; private set; }

    public int Credits { get; private set; }

    public static Course Create(Guid programmeId, string code, string nameEn, string nameAr, string? descriptionEn, string? descriptionAr, int credits)
    {
        var course = new Course
        {
            ProgrammeId = Guard.RequireId(programmeId, nameof(programmeId)),
            Code = Guard.RequireText(code, nameof(code), CodeMaxLength).ToUpperInvariant(),
        };
        course.Amend(nameEn, nameAr, descriptionEn, descriptionAr, credits);
        return course;
    }

    public void Amend(string nameEn, string nameAr, string? descriptionEn, string? descriptionAr, int credits)
    {
        NameEn = Guard.RequireText(nameEn, nameof(nameEn), NameMaxLength);
        NameAr = Guard.RequireText(nameAr, nameof(nameAr), NameMaxLength);
        DescriptionEn = Guard.OptionalText(descriptionEn, nameof(descriptionEn), DescriptionMaxLength);
        DescriptionAr = Guard.OptionalText(descriptionAr, nameof(descriptionAr), DescriptionMaxLength);
        Credits = credits is < 0 or > MaxCredits
            ? throw new DomainException($"credits must be between 0 and {MaxCredits}.")
            : credits;
    }
}
