using FluentValidation;
using OpenCampus.SharedKernel;
using OpenCampus.Sis.Domain.Courses;

namespace OpenCampus.Sis.Application.Courses;

public sealed record CreateCourseRequest(Guid ProgrammeId, string Code, string NameEn, string NameAr, string? DescriptionEn, string? DescriptionAr, int Credits);

public sealed record UpdateCourseRequest(string NameEn, string NameAr, string? DescriptionEn, string? DescriptionAr, int Credits);

/// <summary>Sort accepts code, nameEn, credits, createdAtUtc; prefix '-' for descending.</summary>
public sealed record CourseListQuery(int? Page, int? PageSize, string? Search, Guid? ProgrammeId, string? Sort);

public sealed record CourseResponse(
    Guid Id,
    Guid ProgrammeId,
    string ProgrammeCode,
    string ProgrammeNameEn,
    string ProgrammeNameAr,
    string Code,
    string NameEn,
    string NameAr,
    string? DescriptionEn,
    string? DescriptionAr,
    int Credits,
    DateTime CreatedAtUtc,
    DateTime? ModifiedAtUtc);

public sealed class CreateCourseRequestValidator : AbstractValidator<CreateCourseRequest>
{
    public CreateCourseRequestValidator()
    {
        RuleFor(r => r.ProgrammeId).NotEmpty();
        RuleFor(r => r.Code).NotEmpty().MaximumLength(Course.CodeMaxLength).Matches("^[A-Za-z0-9-]+$");
        RuleFor(r => r.NameEn).NotEmpty().MaximumLength(Course.NameMaxLength);
        RuleFor(r => r.NameAr).NotEmpty().MaximumLength(Course.NameMaxLength);
        RuleFor(r => r.DescriptionEn).MaximumLength(Course.DescriptionMaxLength);
        RuleFor(r => r.DescriptionAr).MaximumLength(Course.DescriptionMaxLength);
        RuleFor(r => r.Credits).InclusiveBetween(0, Course.MaxCredits);
    }
}

public sealed class UpdateCourseRequestValidator : AbstractValidator<UpdateCourseRequest>
{
    public UpdateCourseRequestValidator()
    {
        RuleFor(r => r.NameEn).NotEmpty().MaximumLength(Course.NameMaxLength);
        RuleFor(r => r.NameAr).NotEmpty().MaximumLength(Course.NameMaxLength);
        RuleFor(r => r.DescriptionEn).MaximumLength(Course.DescriptionMaxLength);
        RuleFor(r => r.DescriptionAr).MaximumLength(Course.DescriptionMaxLength);
        RuleFor(r => r.Credits).InclusiveBetween(0, Course.MaxCredits);
    }
}

public sealed class CourseListQueryValidator : AbstractValidator<CourseListQuery>
{
    public static readonly IReadOnlyList<string> SortFields = ["code", "nameEn", "credits", "createdAtUtc"];

    public CourseListQueryValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1).When(q => q.Page.HasValue);
        RuleFor(q => q.PageSize).InclusiveBetween(1, Paging.MaxPageSize).When(q => q.PageSize.HasValue);
        RuleFor(q => q.Search).MaximumLength(100);
        RuleFor(q => q.Sort)
            .Must(s => s is null || SortFields.Contains(s.TrimStart('-'), StringComparer.OrdinalIgnoreCase))
            .WithMessage($"Sort must be one of: {string.Join(", ", SortFields)} (prefix '-' for descending).");
    }
}
