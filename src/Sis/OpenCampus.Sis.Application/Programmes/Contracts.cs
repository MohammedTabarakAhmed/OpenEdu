using FluentValidation;
using OpenCampus.SharedKernel;
using OpenCampus.Sis.Domain.Programmes;

namespace OpenCampus.Sis.Application.Programmes;

// Requests (API-02, SEC-18).

public sealed record CreateProgrammeRequest(string Code, string NameEn, string NameAr, int DurationMonths);

public sealed record UpdateProgrammeRequest(string NameEn, string NameAr, int DurationMonths, bool IsActive);

/// <summary>Collection parameters (API-03). Sort accepts code, nameEn, durationMonths, createdAtUtc; prefix '-' for descending.</summary>
public sealed record ProgrammeListQuery(int? Page, int? PageSize, string? Search, bool? IsActive, string? Sort);

// Responses.

public sealed record ProgrammeResponse(
    Guid Id,
    string Code,
    string NameEn,
    string NameAr,
    int DurationMonths,
    bool IsActive,
    DateTime CreatedAtUtc,
    DateTime? ModifiedAtUtc);

// Validators (18.2): one per request contract.

public sealed class CreateProgrammeRequestValidator : AbstractValidator<CreateProgrammeRequest>
{
    public CreateProgrammeRequestValidator()
    {
        RuleFor(r => r.Code).NotEmpty().MaximumLength(Programme.CodeMaxLength).Matches("^[A-Za-z0-9-]+$");
        RuleFor(r => r.NameEn).NotEmpty().MaximumLength(Programme.NameMaxLength);
        RuleFor(r => r.NameAr).NotEmpty().MaximumLength(Programme.NameMaxLength);
        RuleFor(r => r.DurationMonths).InclusiveBetween(1, 120);
    }
}

public sealed class UpdateProgrammeRequestValidator : AbstractValidator<UpdateProgrammeRequest>
{
    public UpdateProgrammeRequestValidator()
    {
        RuleFor(r => r.NameEn).NotEmpty().MaximumLength(Programme.NameMaxLength);
        RuleFor(r => r.NameAr).NotEmpty().MaximumLength(Programme.NameMaxLength);
        RuleFor(r => r.DurationMonths).InclusiveBetween(1, 120);
    }
}

public sealed class ProgrammeListQueryValidator : AbstractValidator<ProgrammeListQuery>
{
    public static readonly IReadOnlyList<string> SortFields = ["code", "nameEn", "durationMonths", "createdAtUtc"];

    public ProgrammeListQueryValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1).When(q => q.Page.HasValue);
        RuleFor(q => q.PageSize).InclusiveBetween(1, Paging.MaxPageSize).When(q => q.PageSize.HasValue);
        RuleFor(q => q.Search).MaximumLength(100);
        RuleFor(q => q.Sort)
            .Must(s => s is null || SortFields.Contains(s.TrimStart('-'), StringComparer.OrdinalIgnoreCase))
            .WithMessage($"Sort must be one of: {string.Join(", ", SortFields)} (prefix '-' for descending).");
    }
}
