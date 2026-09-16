using FluentValidation;
using OpenCampus.SharedKernel;
using OpenCampus.Sis.Application.Sections;
using OpenCampus.Sis.Domain.Enrolments;

namespace OpenCampus.Sis.Application.Enrolments;

/// <summary>Administrative enrolment of a learner in a section.</summary>
public sealed record CreateEnrolmentRequest(Guid LearnerId, Guid SectionId);

/// <summary>Listing by learner and by section (15.3). At least one of the two is required for administrative listing.</summary>
public sealed record EnrolmentListQuery(int? Page, int? PageSize, Guid? LearnerId, Guid? SectionId, EnrolmentStatus? Status);

public sealed record EnrolmentLearnerSummary(Guid LearnerId, string LearnerNumber, Guid UserId, string? UserName, string? FullNameEn, string? FullNameAr);

public sealed record EnrolmentSectionSummary(
    Guid SectionId, string SectionCode, string TermName, DateOnly StartDate, DateOnly EndDate,
    Guid CourseId, string CourseCode, string CourseNameEn, string CourseNameAr, int Credits, string? InstructorNameEn, string? InstructorNameAr);

public sealed record EnrolmentResponse(
    Guid Id,
    EnrolmentLearnerSummary Learner,
    EnrolmentSectionSummary Section,
    DateTime EnrolledAtUtc,
    EnrolmentStatus Status,
    decimal? FinalGrade,
    DateTime? CompletedAtUtc);

/// <summary>Transcript: every enrolment of a learner with its outcome (15.3 "transcript retrieval").</summary>
public sealed record TranscriptResponse(
    Guid LearnerId,
    string LearnerNumber,
    string? FullNameEn,
    string? FullNameAr,
    IReadOnlyList<EnrolmentResponse> Enrolments,
    int CompletedCount,
    int CreditsEarned);

public sealed class CreateEnrolmentRequestValidator : AbstractValidator<CreateEnrolmentRequest>
{
    public CreateEnrolmentRequestValidator()
    {
        RuleFor(r => r.LearnerId).NotEmpty();
        RuleFor(r => r.SectionId).NotEmpty();
    }
}

public sealed class EnrolmentListQueryValidator : AbstractValidator<EnrolmentListQuery>
{
    public EnrolmentListQueryValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1).When(q => q.Page.HasValue);
        RuleFor(q => q.PageSize).InclusiveBetween(1, Paging.MaxPageSize).When(q => q.PageSize.HasValue);
        RuleFor(q => q.Status).IsInEnum().When(q => q.Status.HasValue);
        RuleFor(q => q).Must(q => q.LearnerId.HasValue || q.SectionId.HasValue)
            .WithName("learnerId").WithMessage("Either learnerId or sectionId is required.");
    }
}
