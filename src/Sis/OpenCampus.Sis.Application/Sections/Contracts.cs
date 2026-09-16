using FluentValidation;
using OpenCampus.SharedKernel;
using OpenCampus.Sis.Domain.Sections;

namespace OpenCampus.Sis.Application.Sections;

// ----- Requests (API-02, SEC-18) -----

public sealed record CreateSectionRequest(
    Guid CourseId, string Code, string TermName, DateOnly StartDate, DateOnly EndDate,
    int Capacity, Guid InstructorUserId, DeliveryMode DeliveryMode);

public sealed record UpdateSectionRequest(
    string TermName, DateOnly StartDate, DateOnly EndDate, int Capacity, Guid InstructorUserId, DeliveryMode DeliveryMode);

/// <summary>Sort accepts code, termName, startDate, status, createdAtUtc; prefix '-' for descending.</summary>
public sealed record SectionListQuery(
    int? Page, int? PageSize, string? Search, Guid? CourseId, Guid? ProgrammeId, SectionStatus? Status, DeliveryMode? DeliveryMode, Guid? InstructorUserId, string? Sort);

public sealed record SessionRequest(DateTime ScheduledStartUtc, DateTime ScheduledEndUtc, string? Location);

public sealed record GradeComponentRequest(string NameEn, string NameAr, decimal WeightPercent, decimal MaxScore);

// ----- Responses -----

public sealed record InstructorSummary(Guid UserId, string UserName, string FullNameEn, string FullNameAr, bool IsActive);

public sealed record SectionResponse(
    Guid Id,
    Guid CourseId,
    string CourseCode,
    string CourseNameEn,
    string CourseNameAr,
    Guid ProgrammeId,
    string Code,
    string TermName,
    DateOnly StartDate,
    DateOnly EndDate,
    int Capacity,
    int ActiveEnrolmentCount,
    InstructorSummary? Instructor,
    DeliveryMode DeliveryMode,
    SectionStatus Status,
    DateTime CreatedAtUtc,
    DateTime? ModifiedAtUtc);

public sealed record SessionResponse(Guid Id, Guid SectionId, DateTime ScheduledStartUtc, DateTime ScheduledEndUtc, string? Location);

public sealed record GradeComponentResponse(Guid Id, Guid SectionId, string NameEn, string NameAr, decimal WeightPercent, decimal MaxScore);

public sealed record SectionDetailResponse(
    SectionResponse Section,
    IReadOnlyList<SessionResponse> Sessions,
    IReadOnlyList<GradeComponentResponse> GradeComponents,
    decimal TotalWeightPercent);

// ----- Validators (18.2) -----

public sealed class CreateSectionRequestValidator : AbstractValidator<CreateSectionRequest>
{
    public CreateSectionRequestValidator()
    {
        RuleFor(r => r.CourseId).NotEmpty();
        RuleFor(r => r.Code).NotEmpty().MaximumLength(CourseSection.CodeMaxLength).Matches("^[A-Za-z0-9-]+$");
        RuleFor(r => r.TermName).NotEmpty().MaximumLength(CourseSection.TermNameMaxLength);
        RuleFor(r => r.EndDate).GreaterThanOrEqualTo(r => r.StartDate).WithMessage("EndDate must not precede StartDate.");
        RuleFor(r => r.Capacity).InclusiveBetween(1, CourseSection.MaxCapacity);
        RuleFor(r => r.InstructorUserId).NotEmpty();
        RuleFor(r => r.DeliveryMode).IsInEnum();
    }
}

public sealed class UpdateSectionRequestValidator : AbstractValidator<UpdateSectionRequest>
{
    public UpdateSectionRequestValidator()
    {
        RuleFor(r => r.TermName).NotEmpty().MaximumLength(CourseSection.TermNameMaxLength);
        RuleFor(r => r.EndDate).GreaterThanOrEqualTo(r => r.StartDate).WithMessage("EndDate must not precede StartDate.");
        RuleFor(r => r.Capacity).InclusiveBetween(1, CourseSection.MaxCapacity);
        RuleFor(r => r.InstructorUserId).NotEmpty();
        RuleFor(r => r.DeliveryMode).IsInEnum();
    }
}

public sealed class SectionListQueryValidator : AbstractValidator<SectionListQuery>
{
    public static readonly IReadOnlyList<string> SortFields = ["code", "termName", "startDate", "status", "createdAtUtc"];

    public SectionListQueryValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1).When(q => q.Page.HasValue);
        RuleFor(q => q.PageSize).InclusiveBetween(1, Paging.MaxPageSize).When(q => q.PageSize.HasValue);
        RuleFor(q => q.Search).MaximumLength(100);
        RuleFor(q => q.Status).IsInEnum().When(q => q.Status.HasValue);
        RuleFor(q => q.DeliveryMode).IsInEnum().When(q => q.DeliveryMode.HasValue);
        RuleFor(q => q.Sort)
            .Must(s => s is null || SortFields.Contains(s.TrimStart('-'), StringComparer.OrdinalIgnoreCase))
            .WithMessage($"Sort must be one of: {string.Join(", ", SortFields)} (prefix '-' for descending).");
    }
}

public sealed class SessionRequestValidator : AbstractValidator<SessionRequest>
{
    public SessionRequestValidator()
    {
        RuleFor(r => r.ScheduledEndUtc).GreaterThan(r => r.ScheduledStartUtc).WithMessage("ScheduledEndUtc must be later than ScheduledStartUtc.");
        RuleFor(r => r.Location).MaximumLength(Session.LocationMaxLength);
    }
}

public sealed class GradeComponentRequestValidator : AbstractValidator<GradeComponentRequest>
{
    public GradeComponentRequestValidator()
    {
        RuleFor(r => r.NameEn).NotEmpty().MaximumLength(GradeComponent.NameMaxLength);
        RuleFor(r => r.NameAr).NotEmpty().MaximumLength(GradeComponent.NameMaxLength);
        RuleFor(r => r.WeightPercent).GreaterThan(0).LessThanOrEqualTo(100).PrecisionScale(5, 2, true);
        RuleFor(r => r.MaxScore).GreaterThan(0).PrecisionScale(7, 2, true);
    }
}
