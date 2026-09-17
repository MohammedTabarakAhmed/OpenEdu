using FluentValidation;
using OpenCampus.Lms.Application.Abstractions;
using OpenCampus.Lms.Domain.Assessment;
using OpenCampus.Lms.Domain.Attendance;

namespace OpenCampus.Lms.Application.Assessment;

// ----- Requests (API-02, SEC-18) -----

public sealed record AssignmentRequest(
    string TitleEn, string TitleAr, string? Instructions, decimal MaxScore, DateTime DueAtUtc, bool AllowLate, decimal LatePenaltyPercent);

/// <summary>A learner's work: text, a file (multipart, host-mapped), or both.</summary>
public sealed record SubmitWorkRequest(string? TextBody);

public sealed record MarkSubmissionRequest(decimal Score, string? Feedback);

public sealed record AttendanceEntry(Guid LearnerUserId, AttendanceStatus Status);

/// <summary>The register for one session: one status per learner named; unnamed learners are left unrecorded.</summary>
public sealed record RecordAttendanceRequest(IReadOnlyList<AttendanceEntry> Entries);

// ----- Responses -----

public sealed record SubmissionResponse(
    Guid Id,
    Guid AssignmentId,
    Guid LearnerUserId,
    string? LearnerNumber,
    string? LearnerNameEn,
    string? LearnerNameAr,
    DateTime SubmittedAtUtc,
    bool IsLate,
    string? TextBody,
    bool HasFile,
    decimal? Score,
    string? Feedback,
    DateTime? GradedAtUtc,
    decimal? OriginalityScore);

public sealed record AssignmentResponse(
    Guid Id,
    Guid SectionId,
    string TitleEn,
    string TitleAr,
    string? Instructions,
    decimal MaxScore,
    DateTime DueAtUtc,
    bool AllowLate,
    decimal LatePenaltyPercent,
    bool IsPublished,
    int SubmissionCount,
    int MarkedCount,
    /// <summary>The caller's own submission when the caller is a learner; null for managers.</summary>
    SubmissionResponse? MySubmission);

public sealed record SectionAssignmentsResponse(SectionSummary Section, bool CanManage, IReadOnlyList<AssignmentResponse> Assignments);

/// <summary>Submissions of one assignment against the section's enrolled learners, so unsubmitted learners appear too.</summary>
public sealed record AssignmentSubmissionsResponse(AssignmentResponse Assignment, IReadOnlyList<EnrolledLearner> EnrolledLearners, IReadOnlyList<SubmissionResponse> Submissions);

public sealed record AttendanceRecordResponse(Guid SessionId, Guid LearnerUserId, AttendanceStatus Status, Guid RecordedByUserId, DateTime RecordedAtUtc);

/// <summary>One session's register: the enrolled learners and whatever has been recorded (15.3 "attendance recording and retrieval").</summary>
public sealed record SessionRegisterResponse(SessionSummary Session, IReadOnlyList<EnrolledLearner> Learners, IReadOnlyList<AttendanceRecordResponse> Records);

public sealed record LearnerAttendanceSummary(Guid LearnerUserId, int SessionsHeld, int SessionsAttended, decimal AttendancePercent);

/// <summary>Attendance across a section: sessions, and per learner the rate that feeds BR-12.</summary>
public sealed record SectionAttendanceResponse(SectionSummary Section, bool CanManage, IReadOnlyList<SessionSummary> Sessions, IReadOnlyList<LearnerAttendanceSummary> Learners, IReadOnlyList<AttendanceRecordResponse> Records);

// ----- Validators (18.2) -----

public sealed class AssignmentRequestValidator : AbstractValidator<AssignmentRequest>
{
    public AssignmentRequestValidator()
    {
        RuleFor(r => r.TitleEn).NotEmpty().MaximumLength(Assignment.TitleMaxLength);
        RuleFor(r => r.TitleAr).NotEmpty().MaximumLength(Assignment.TitleMaxLength);
        RuleFor(r => r.Instructions).MaximumLength(Assignment.InstructionsMaxLength);
        RuleFor(r => r.MaxScore).GreaterThan(0).LessThanOrEqualTo(Assignment.MaxPossibleScore).PrecisionScale(7, 2, true);
        RuleFor(r => r.LatePenaltyPercent).InclusiveBetween(0, 100).PrecisionScale(5, 2, true);
    }
}

public sealed class SubmitWorkRequestValidator : AbstractValidator<SubmitWorkRequest>
{
    public SubmitWorkRequestValidator()
    {
        RuleFor(r => r.TextBody).MaximumLength(Submission.TextBodyMaxLength);
    }
}

public sealed class MarkSubmissionRequestValidator : AbstractValidator<MarkSubmissionRequest>
{
    public MarkSubmissionRequestValidator()
    {
        RuleFor(r => r.Score).GreaterThanOrEqualTo(0).PrecisionScale(7, 2, true);
        RuleFor(r => r.Feedback).MaximumLength(Submission.FeedbackMaxLength);
    }
}

public sealed class RecordAttendanceRequestValidator : AbstractValidator<RecordAttendanceRequest>
{
    public RecordAttendanceRequestValidator()
    {
        RuleFor(r => r.Entries).NotEmpty();
        RuleForEach(r => r.Entries).ChildRules(entry =>
        {
            entry.RuleFor(e => e.LearnerUserId).NotEmpty();
            entry.RuleFor(e => e.Status).IsInEnum();
        });
        RuleFor(r => r.Entries).Must(e => e.Select(x => x.LearnerUserId).Distinct().Count() == e.Count)
            .WithMessage("Each learner may appear once.");
    }
}

internal static class AssessmentMapping
{
    public static SubmissionResponse ToResponse(this Submission s, Assignment assignment, EnrolledLearner? learner) => new(
        s.Id, s.AssignmentId, s.LearnerUserId, learner?.LearnerNumber, learner?.FullNameEn, learner?.FullNameAr,
        s.SubmittedAtUtc, assignment.IsLate(s.SubmittedAtUtc), s.TextBody, s.StoredPath is not null,
        s.Score, s.Feedback, s.GradedAtUtc, s.OriginalityScore);

    public static AssignmentResponse ToResponse(this Assignment a, SubmissionResponse? mine = null) => new(
        a.Id, a.SectionId, a.TitleEn, a.TitleAr, a.Instructions, a.MaxScore, a.DueAtUtc, a.AllowLate, a.LatePenaltyPercent, a.IsPublished,
        a.Submissions.Count, a.Submissions.Count(s => s.IsGraded), mine);

    public static AttendanceRecordResponse ToResponse(this AttendanceRecord r) => new(r.SessionId, r.LearnerUserId, r.Status, r.RecordedByUserId, r.RecordedAtUtc);
}
