using FluentValidation;
using OpenCampus.Sis.Domain.Enrolments;

namespace OpenCampus.Sis.Application.Grading;

// ----- Requests (API-02, SEC-18) -----

/// <summary>Records or amends the score of one enrolment against one component of the section's scheme.</summary>
public sealed record RecordGradeRequest(Guid EnrolmentId, Guid GradeComponentId, decimal Score);

// ----- Responses -----

public sealed record GradeEntryResponse(
    Guid Id,
    Guid EnrolmentId,
    Guid GradeComponentId,
    decimal Score,
    bool IsReleased,
    Guid GradedByUserId,
    DateTime GradedAtUtc);

public sealed record GradebookComponent(Guid Id, string NameEn, string NameAr, decimal WeightPercent, decimal MaxScore);

public sealed record GradebookRow(
    Guid EnrolmentId,
    Guid LearnerId,
    string LearnerNumber,
    Guid UserId,
    string FullNameEn,
    string FullNameAr,
    EnrolmentStatus Status,
    decimal? FinalGrade,
    IReadOnlyList<GradeEntryResponse> Entries);

/// <summary>The section's gradebook: every active enrolment × every component (15.3 "section gradebook retrieval").</summary>
public sealed record GradebookResponse(
    Guid SectionId,
    string SectionCode,
    string CourseCode,
    string CourseNameEn,
    string CourseNameAr,
    IReadOnlyList<GradebookComponent> Components,
    IReadOnlyList<GradebookRow> Rows,
    int UngradedPairCount,
    bool IsReleased);

/// <summary>A learner's own released results for one enrolment (15.3 "released grade retrieval"; BR-06).</summary>
public sealed record LearnerResultsResponse(
    Guid EnrolmentId,
    Guid SectionId,
    string SectionCode,
    string CourseCode,
    string CourseNameEn,
    string CourseNameAr,
    EnrolmentStatus Status,
    decimal? FinalGrade,
    bool IsReleased,
    IReadOnlyList<GradebookComponent> Components,
    IReadOnlyList<GradeEntryResponse> Entries);

// ----- Validators (18.2) -----

public sealed class RecordGradeRequestValidator : AbstractValidator<RecordGradeRequest>
{
    public RecordGradeRequestValidator()
    {
        RuleFor(r => r.EnrolmentId).NotEmpty();
        RuleFor(r => r.GradeComponentId).NotEmpty();
        RuleFor(r => r.Score).GreaterThanOrEqualTo(0).PrecisionScale(7, 2, true);
    }
}
