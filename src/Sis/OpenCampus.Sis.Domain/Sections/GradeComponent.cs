using OpenCampus.SharedKernel;

namespace OpenCampus.Sis.Domain.Sections;

/// <summary>A weighted element of a section's assessment scheme (SDD 13.3, Appendix D). Part of the section aggregate.</summary>
public sealed class GradeComponent : Entity
{
    public const int NameMaxLength = 200;

    private GradeComponent()
    {
    }

    public Guid SectionId { get; private set; }

    public string NameEn { get; private set; } = null!;

    public string NameAr { get; private set; } = null!;

    /// <summary>DC-05: exact decimal, precision 5 scale 2.</summary>
    public decimal WeightPercent { get; private set; }

    /// <summary>DC-05: exact decimal, precision 7 scale 2.</summary>
    public decimal MaxScore { get; private set; }

    internal static GradeComponent Create(Guid sectionId, string nameEn, string nameAr, decimal weightPercent, decimal maxScore)
    {
        var component = new GradeComponent { SectionId = sectionId };
        component.Amend(nameEn, nameAr, weightPercent, maxScore);
        return component;
    }

    internal void Amend(string nameEn, string nameAr, decimal weightPercent, decimal maxScore)
    {
        NameEn = Guard.RequireText(nameEn, nameof(nameEn), NameMaxLength);
        NameAr = Guard.RequireText(nameAr, nameof(nameAr), NameMaxLength);
        WeightPercent = weightPercent is <= 0 or > 100
            ? throw new DomainException("weightPercent must be greater than 0 and at most 100.")
            : weightPercent;
        MaxScore = maxScore <= 0 ? throw new DomainException("maxScore must be greater than 0.") : maxScore;
    }
}
