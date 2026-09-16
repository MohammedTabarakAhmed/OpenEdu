using FluentValidation;
using OpenCampus.SharedKernel;
using OpenCampus.Sis.Domain.Learners;

namespace OpenCampus.Sis.Application.Learners;

public sealed record CreateLearnerRequest(Guid UserId, string LearnerNumber, string? NationalId, DateOnly? DateOfBirth, Gender Gender, string? Phone);

public sealed record UpdateLearnerRequest(string? NationalId, DateOnly? DateOfBirth, Gender Gender, string? Phone, LearnerStatus Status);

/// <summary>Sort accepts learnerNumber, status, createdAtUtc; prefix '-' for descending. Search matches learner number, user name and full name.</summary>
public sealed record LearnerListQuery(int? Page, int? PageSize, string? Search, LearnerStatus? Status, string? Sort);

public sealed record LearnerUserSummary(Guid UserId, string UserName, string Email, string FullNameEn, string FullNameAr, bool IsActive);

/// <summary>NationalId is returned only on single retrieval (18.1: never logged; not spread across list responses).</summary>
public sealed record LearnerResponse(
    Guid Id,
    LearnerUserSummary? User,
    string LearnerNumber,
    string? NationalId,
    DateOnly? DateOfBirth,
    Gender Gender,
    string? Phone,
    LearnerStatus Status,
    DateTime CreatedAtUtc,
    DateTime? ModifiedAtUtc);

public sealed class CreateLearnerRequestValidator : AbstractValidator<CreateLearnerRequest>
{
    public CreateLearnerRequestValidator()
    {
        RuleFor(r => r.UserId).NotEmpty();
        RuleFor(r => r.LearnerNumber).NotEmpty().MaximumLength(Learner.LearnerNumberMaxLength).Matches("^[A-Za-z0-9-]+$");
        RuleFor(r => r.NationalId).MaximumLength(Learner.NationalIdMaxLength);
        RuleFor(r => r.Phone).MaximumLength(Learner.PhoneMaxLength);
        RuleFor(r => r.Gender).IsInEnum();
        RuleFor(r => r.DateOfBirth).LessThan(_ => DateOnly.FromDateTime(DateTime.UtcNow)).When(r => r.DateOfBirth.HasValue);
    }
}

public sealed class UpdateLearnerRequestValidator : AbstractValidator<UpdateLearnerRequest>
{
    public UpdateLearnerRequestValidator()
    {
        RuleFor(r => r.NationalId).MaximumLength(Learner.NationalIdMaxLength);
        RuleFor(r => r.Phone).MaximumLength(Learner.PhoneMaxLength);
        RuleFor(r => r.Gender).IsInEnum();
        RuleFor(r => r.Status).IsInEnum();
        RuleFor(r => r.DateOfBirth).LessThan(_ => DateOnly.FromDateTime(DateTime.UtcNow)).When(r => r.DateOfBirth.HasValue);
    }
}

public sealed class LearnerListQueryValidator : AbstractValidator<LearnerListQuery>
{
    public static readonly IReadOnlyList<string> SortFields = ["learnerNumber", "status", "createdAtUtc"];

    public LearnerListQueryValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1).When(q => q.Page.HasValue);
        RuleFor(q => q.PageSize).InclusiveBetween(1, Paging.MaxPageSize).When(q => q.PageSize.HasValue);
        RuleFor(q => q.Search).MaximumLength(100);
        RuleFor(q => q.Status).IsInEnum().When(q => q.Status.HasValue);
        RuleFor(q => q.Sort)
            .Must(s => s is null || SortFields.Contains(s.TrimStart('-'), StringComparer.OrdinalIgnoreCase))
            .WithMessage($"Sort must be one of: {string.Join(", ", SortFields)} (prefix '-' for descending).");
    }
}
