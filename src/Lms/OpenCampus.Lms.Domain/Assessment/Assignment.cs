using OpenCampus.SharedKernel;

namespace OpenCampus.Lms.Domain.Assessment;

/// <summary>
/// Aggregate root for an assessed task set for a section (SDD 13.4: Assignment 1—* Submission). Owns BR-08
/// (late submission) and BR-09 (active enrolment required); marking applies the configured late penalty.
/// SectionId and every user reference are cross-module identifiers (MB-03); whether a learner holds an
/// active enrolment is established through the LMS→SIS contract and supplied by the caller.
/// </summary>
public sealed class Assignment : Entity
{
    public const int TitleMaxLength = 200;
    public const int InstructionsMaxLength = 20000;
    public const decimal MaxPossibleScore = 1000m;

    private readonly List<Submission> _submissions = [];

    private Assignment()
    {
    }

    public Guid SectionId { get; private set; }

    public string TitleEn { get; private set; } = null!;

    public string TitleAr { get; private set; } = null!;

    public string? Instructions { get; private set; }

    /// <summary>DC-05: exact decimal, precision 7 scale 2.</summary>
    public decimal MaxScore { get; private set; }

    public DateTime DueAtUtc { get; private set; }

    public bool AllowLate { get; private set; }

    /// <summary>DC-05: exact decimal, precision 5 scale 2. Deducted from a late submission's score when late work is allowed (BR-08).</summary>
    public decimal LatePenaltyPercent { get; private set; }

    public bool IsPublished { get; private set; }

    public IReadOnlyCollection<Submission> Submissions => _submissions.AsReadOnly();

    public static Assignment Create(Guid sectionId, string titleEn, string titleAr, string? instructions, decimal maxScore, DateTime dueAtUtc, bool allowLate, decimal latePenaltyPercent)
    {
        var assignment = new Assignment { SectionId = Guard.RequireId(sectionId, nameof(sectionId)) };
        assignment.Amend(titleEn, titleAr, instructions, maxScore, dueAtUtc, allowLate, latePenaltyPercent);
        return assignment;
    }

    /// <summary>The maximum score is frozen once any submission has been marked, so recorded scores keep their meaning.</summary>
    public void Amend(string titleEn, string titleAr, string? instructions, decimal maxScore, DateTime dueAtUtc, bool allowLate, decimal latePenaltyPercent)
    {
        if (maxScore <= 0 || maxScore > MaxPossibleScore)
        {
            throw new DomainException($"maxScore must be greater than 0 and at most {MaxPossibleScore}.");
        }

        if (latePenaltyPercent is < 0 or > 100)
        {
            throw new DomainException("latePenaltyPercent must be between 0 and 100.");
        }

        if (_submissions.Any(s => s.IsGraded) && maxScore != MaxScore)
        {
            throw new DomainException("The maximum score cannot change once submissions have been marked.");
        }

        TitleEn = Guard.RequireText(titleEn, nameof(titleEn), TitleMaxLength);
        TitleAr = Guard.RequireText(titleAr, nameof(titleAr), TitleMaxLength);
        Instructions = Guard.OptionalText(instructions, nameof(instructions), InstructionsMaxLength);
        MaxScore = maxScore;
        DueAtUtc = DateTime.SpecifyKind(dueAtUtc, DateTimeKind.Utc);
        AllowLate = allowLate;
        LatePenaltyPercent = latePenaltyPercent;
    }

    /// <summary>Assignment publication (15.3): learners see and submit to published assignments only.</summary>
    public void Publish() => IsPublished = true;

    /// <summary>Withdrawing publication is refused once work has been submitted against it.</summary>
    public void Unpublish()
    {
        if (_submissions.Count > 0)
        {
            throw new DomainException("An assignment with submissions cannot be unpublished.");
        }

        IsPublished = false;
    }

    public bool IsLate(DateTime submittedAtUtc) => submittedAtUtc > DueAtUtc;

    // ----- Submissions (15.3 "submission creation"); BR-09 then BR-08 -----

    /// <summary>
    /// Accepts a learner's work. A learner has at most one submission (13.4 unique index); resubmitting before
    /// marking replaces the earlier work under the same rules. BR-09: an active enrolment in the section is
    /// required. BR-08: after the due date the submission is refused unless late work is allowed.
    /// </summary>
    public Submission Submit(Guid learnerUserId, bool learnerHasActiveEnrolment, string? textBody, string? storedPath, DateTime utcNow)
    {
        if (!IsPublished)
        {
            throw new DomainException("The assignment is not published.");
        }

        if (!learnerHasActiveEnrolment)
        {
            throw BusinessRules.Br09NoActiveEnrolment();
        }

        if (IsLate(utcNow) && !AllowLate)
        {
            throw BusinessRules.Br08LateSubmissionDisallowed();
        }

        var existing = FindSubmissionByLearner(learnerUserId);
        if (existing is null)
        {
            var submission = Submission.Create(Id, learnerUserId, textBody, storedPath, utcNow);
            _submissions.Add(submission);
            return submission;
        }

        existing.Replace(textBody, storedPath, utcNow);
        return existing;
    }

    // ----- Marking (15.3 "submission marking") -----

    /// <summary>
    /// Records the mark. The raw score is bounded by the maximum; a late submission (where late work is allowed)
    /// attracts the configured penalty (BR-08), so the recorded score is the penalised one.
    /// </summary>
    public Submission Mark(Guid submissionId, decimal rawScore, string? feedback, Guid gradedByUserId, DateTime utcNow)
    {
        var submission = FindSubmission(submissionId);
        if (rawScore < 0 || rawScore > MaxScore)
        {
            throw new DomainException($"The score must be between 0 and the assignment maximum of {MaxScore}.");
        }

        var score = IsLate(submission.SubmittedAtUtc)
            ? Math.Round(rawScore * (1 - LatePenaltyPercent / 100m), 2, MidpointRounding.AwayFromZero)
            : rawScore;
        submission.RecordMark(score, feedback, gradedByUserId, utcNow);
        return submission;
    }

    public Submission? FindSubmissionByLearner(Guid learnerUserId) => _submissions.SingleOrDefault(s => s.LearnerUserId == learnerUserId);

    public Submission? FindSubmissionOrDefault(Guid submissionId) => _submissions.SingleOrDefault(s => s.Id == submissionId);

    private Submission FindSubmission(Guid submissionId) =>
        FindSubmissionOrDefault(submissionId) ?? throw new EntityNotFoundException(nameof(Submission), submissionId);
}

/// <summary>
/// A learner's work for an assignment (SDD 13.4; unique on (AssignmentId, LearnerUserId)). Part of the assignment
/// aggregate. StoredPath follows 18.5 and SEC-23; OriginalityScore is populated by the EXT-03 adapter when configured.
/// </summary>
public sealed class Submission : Entity
{
    public const int TextBodyMaxLength = 20000;
    public const int StoredPathMaxLength = 400;
    public const int FeedbackMaxLength = 4000;

    private Submission()
    {
    }

    public Guid AssignmentId { get; private set; }

    public Guid LearnerUserId { get; private set; }

    public DateTime SubmittedAtUtc { get; private set; }

    public string? TextBody { get; private set; }

    public string? StoredPath { get; private set; }

    /// <summary>DC-05: exact decimal, precision 7 scale 2. The penalised score once marked.</summary>
    public decimal? Score { get; private set; }

    public string? Feedback { get; private set; }

    public Guid? GradedByUserId { get; private set; }

    public DateTime? GradedAtUtc { get; private set; }

    /// <summary>DC-05: exact decimal, precision 5 scale 2.</summary>
    public decimal? OriginalityScore { get; private set; }

    public bool IsGraded => Score.HasValue;

    internal static Submission Create(Guid assignmentId, Guid learnerUserId, string? textBody, string? storedPath, DateTime utcNow)
    {
        var submission = new Submission
        {
            AssignmentId = Guard.RequireId(assignmentId, nameof(assignmentId)),
            LearnerUserId = Guard.RequireId(learnerUserId, nameof(learnerUserId)),
        };
        submission.Replace(textBody, storedPath, utcNow);
        return submission;
    }

    internal void Replace(string? textBody, string? storedPath, DateTime utcNow)
    {
        if (IsGraded)
        {
            throw new DomainException("A marked submission cannot be replaced.");
        }

        var text = Guard.OptionalText(textBody, nameof(textBody), TextBodyMaxLength);
        var path = Guard.OptionalText(storedPath, nameof(storedPath), StoredPathMaxLength);
        if (text is null && path is null)
        {
            throw new DomainException("A submission needs a text body, a file, or both.");
        }

        if (path is not null && (Path.IsPathRooted(path) || path.Contains("..", StringComparison.Ordinal)))
        {
            throw new DomainException("storedPath must be a relative path within the file store.");
        }

        TextBody = text;
        StoredPath = path;
        SubmittedAtUtc = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
    }

    internal void RecordMark(decimal score, string? feedback, Guid gradedByUserId, DateTime utcNow)
    {
        Score = score;
        Feedback = Guard.OptionalText(feedback, nameof(feedback), FeedbackMaxLength);
        GradedByUserId = Guard.RequireId(gradedByUserId, nameof(gradedByUserId));
        GradedAtUtc = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
    }

    /// <summary>Result of the originality check (EXT-03), recorded when an adapter is configured.</summary>
    public void RecordOriginality(decimal originalityScore)
    {
        OriginalityScore = originalityScore is < 0 or > 100
            ? throw new DomainException("originalityScore must be between 0 and 100.")
            : originalityScore;
    }
}
