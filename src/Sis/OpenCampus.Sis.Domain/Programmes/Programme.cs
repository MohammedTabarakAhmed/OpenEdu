using OpenCampus.SharedKernel;

namespace OpenCampus.Sis.Domain.Programmes;

/// <summary>Aggregate root for a programme of study (SDD 13.3).</summary>
public sealed class Programme : Entity
{
    public const int CodeMaxLength = 20;
    public const int NameMaxLength = 200;

    private Programme()
    {
    }

    public string Code { get; private set; } = null!;

    public string NameEn { get; private set; } = null!;

    public string NameAr { get; private set; } = null!;

    public int DurationMonths { get; private set; }

    public bool IsActive { get; private set; }

    public static Programme Create(string code, string nameEn, string nameAr, int durationMonths)
    {
        return new Programme
        {
            Code = Guard.RequireText(code, nameof(code), CodeMaxLength).ToUpperInvariant(),
            NameEn = Guard.RequireText(nameEn, nameof(nameEn), NameMaxLength),
            NameAr = Guard.RequireText(nameAr, nameof(nameAr), NameMaxLength),
            DurationMonths = Guard.RequirePositive(durationMonths, nameof(durationMonths)),
            IsActive = true,
        };
    }

    public void Amend(string nameEn, string nameAr, int durationMonths)
    {
        NameEn = Guard.RequireText(nameEn, nameof(nameEn), NameMaxLength);
        NameAr = Guard.RequireText(nameAr, nameof(nameAr), NameMaxLength);
        DurationMonths = Guard.RequirePositive(durationMonths, nameof(durationMonths));
    }

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;
}
