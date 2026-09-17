using OpenCampus.Lms.Domain.Assessment;
using OpenCampus.Lms.Domain.Attendance;
using OpenCampus.SharedKernel;

namespace OpenCampus.UnitTests.Lms.Domain;

/// <summary>Assessment rules owned by the LMS aggregates: BR-08 and BR-09 (assignment), BR-13 (attendance record).</summary>
public class AssessmentRulesTests
{
    private static readonly Guid SectionId = Guid.NewGuid();
    private static readonly Guid LearnerA = Guid.NewGuid();
    private static readonly Guid LearnerB = Guid.NewGuid();
    private static readonly Guid Grader = Guid.NewGuid();
    private static readonly DateTime Due = new(2026, 10, 15, 23, 59, 0, DateTimeKind.Utc);

    private static Assignment Published(bool allowLate = false, decimal penalty = 0m, decimal maxScore = 100m)
    {
        var assignment = Assignment.Create(SectionId, "Essay", "مقال", "Write 500 words.", maxScore, Due, allowLate, penalty);
        assignment.Publish();
        return assignment;
    }

    // ----- Creation -----

    [Fact]
    public void Create_ValidatesScoreBoundsPenaltyAndTitles()
    {
        Should.Throw<DomainException>(() => Assignment.Create(Guid.Empty, "T", "ع", null, 100m, Due, false, 0m));
        Should.Throw<DomainException>(() => Assignment.Create(SectionId, "", "ع", null, 100m, Due, false, 0m));
        Should.Throw<DomainException>(() => Assignment.Create(SectionId, "T", "ع", null, 0m, Due, false, 0m));
        Should.Throw<DomainException>(() => Assignment.Create(SectionId, "T", "ع", null, Assignment.MaxPossibleScore + 1, Due, false, 0m));
        Should.Throw<DomainException>(() => Assignment.Create(SectionId, "T", "ع", null, 100m, Due, true, 101m));

        var assignment = Assignment.Create(SectionId, "T", "ع", null, 100m, Due, true, 10m);
        assignment.IsPublished.ShouldBeFalse();
        assignment.Submissions.ShouldBeEmpty();
    }

    [Fact]
    public void Submit_RequiresPublication()
    {
        var draft = Assignment.Create(SectionId, "T", "ع", null, 100m, Due, false, 0m);

        Should.Throw<DomainException>(() => draft.Submit(LearnerA, true, "text", null, Due.AddDays(-1)));
    }

    // ----- BR-09: a submission requires an active enrolment in the assignment's section -----

    [Fact]
    public void BR09_SubmissionWithoutActiveEnrolment_IsRefused()
    {
        var assignment = Published();

        var violation = Should.Throw<BusinessRuleViolationException>(() => assignment.Submit(LearnerA, learnerHasActiveEnrolment: false, "text", null, Due.AddDays(-1)));

        violation.RuleCode.ShouldBe("BR-09");
        assignment.Submissions.ShouldBeEmpty();
    }

    // ----- BR-08: late submission refused unless allowed; otherwise penalised -----

    [Fact]
    public void BR08_LateSubmission_IsRefusedWhenLateWorkIsDisallowed()
    {
        var assignment = Published(allowLate: false);

        var violation = Should.Throw<BusinessRuleViolationException>(() => assignment.Submit(LearnerA, true, "text", null, Due.AddMinutes(1)));

        violation.RuleCode.ShouldBe("BR-08");
        assignment.Submissions.ShouldBeEmpty();
    }

    [Fact]
    public void BR08_OnTimeSubmission_IsAccepted_AtTheDeadlineItself()
    {
        var assignment = Published(allowLate: false);

        var submission = assignment.Submit(LearnerA, true, "text", null, Due);

        submission.SubmittedAtUtc.ShouldBe(Due);
        assignment.IsLate(submission.SubmittedAtUtc).ShouldBeFalse();
    }

    [Fact]
    public void BR08_LateSubmission_WhenAllowed_AttractsThePenaltyAtMarking()
    {
        var assignment = Published(allowLate: true, penalty: 20m);
        var late = assignment.Submit(LearnerA, true, "late text", null, Due.AddHours(5));
        var onTime = assignment.Submit(LearnerB, true, "on time", null, Due.AddHours(-5));

        assignment.Mark(late.Id, 80m, "Good but late", Grader, Due.AddDays(3));
        assignment.Mark(onTime.Id, 80m, "Good", Grader, Due.AddDays(3));

        late.Score.ShouldBe(64m); // 80 × (1 − 0.20)
        onTime.Score.ShouldBe(80m);
        late.GradedByUserId.ShouldBe(Grader);
        late.Feedback.ShouldBe("Good but late");
    }

    // ----- Resubmission and marking -----

    [Fact]
    public void Resubmit_ReplacesUnmarkedWork_AndIsRefusedOnceMarked()
    {
        var assignment = Published();
        var first = assignment.Submit(LearnerA, true, "v1", null, Due.AddDays(-2));

        var second = assignment.Submit(LearnerA, true, "v2", "submissions/x/y.pdf", Due.AddDays(-1));

        second.ShouldBeSameAs(first); // one row per (assignment, learner)
        second.TextBody.ShouldBe("v2");
        second.StoredPath.ShouldBe("submissions/x/y.pdf");
        assignment.Submissions.Count.ShouldBe(1);

        assignment.Mark(first.Id, 50m, null, Grader, Due);
        Should.Throw<DomainException>(() => assignment.Submit(LearnerA, true, "v3", null, Due.AddDays(-1)));
    }

    [Fact]
    public void Submit_RequiresTextOrFile_AndARelativeStoredPath()
    {
        var assignment = Published();

        Should.Throw<DomainException>(() => assignment.Submit(LearnerA, true, null, null, Due));
        Should.Throw<DomainException>(() => assignment.Submit(LearnerA, true, null, "../escape.pdf", Due));
        Should.Throw<DomainException>(() => assignment.Submit(LearnerA, true, null, "C:\\abs.pdf", Due));
        assignment.Submit(LearnerA, true, null, "submissions/a/b.pdf", Due).TextBody.ShouldBeNull();
    }

    [Fact]
    public void Mark_BoundsTheScore_AndReportsUnknownSubmissions()
    {
        var assignment = Published(maxScore: 50m);
        var submission = assignment.Submit(LearnerA, true, "text", null, Due);

        Should.Throw<DomainException>(() => assignment.Mark(submission.Id, 50.01m, null, Grader, Due));
        Should.Throw<DomainException>(() => assignment.Mark(submission.Id, -1m, null, Grader, Due));
        Should.Throw<EntityNotFoundException>(() => assignment.Mark(Guid.NewGuid(), 10m, null, Grader, Due));

        assignment.Mark(submission.Id, 50m, null, Grader, Due);
        submission.IsGraded.ShouldBeTrue();
    }

    [Fact]
    public void Amend_FreezesMaxScoreOnceMarked_AndUnpublishRefusedWithSubmissions()
    {
        var assignment = Published();
        var submission = assignment.Submit(LearnerA, true, "text", null, Due);
        Should.Throw<DomainException>(assignment.Unpublish);

        assignment.Amend("Essay 2", "مقال 2", null, 100m, Due.AddDays(1), true, 5m);
        assignment.TitleEn.ShouldBe("Essay 2");

        assignment.Mark(submission.Id, 70m, null, Grader, Due);
        Should.Throw<DomainException>(() => assignment.Amend("Essay 3", "مقال 3", null, 200m, Due, true, 5m));
        assignment.Amend("Essay 3", "مقال 3", null, 100m, Due, true, 5m);
    }

    [Fact]
    public void Originality_IsBoundedTo0To100()
    {
        var assignment = Published();
        var submission = assignment.Submit(LearnerA, true, "text", null, Due);

        submission.RecordOriginality(12.5m);
        submission.OriginalityScore.ShouldBe(12.5m);
        Should.Throw<DomainException>(() => submission.RecordOriginality(100.5m));
    }

    // ----- BR-13: no attendance record for a session scheduled in the future -----

    [Fact]
    public void BR13_AttendanceForFutureSession_IsRefused()
    {
        var now = new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);

        var violation = Should.Throw<BusinessRuleViolationException>(() =>
            AttendanceRecord.Create(Guid.NewGuid(), sessionScheduledStartUtc: now.AddMinutes(1), LearnerA, AttendanceStatus.Present, Grader, now));

        violation.RuleCode.ShouldBe("BR-13");
    }

    [Fact]
    public void BR13_AttendanceForStartedSession_IsAccepted_AndAmendable()
    {
        var now = new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc);

        var record = AttendanceRecord.Create(Guid.NewGuid(), now, LearnerA, AttendanceStatus.Absent, Grader, now);
        record.CountsAsAttended.ShouldBeFalse();

        record.Amend(AttendanceStatus.Late, Grader, now.AddHours(1));
        record.Status.ShouldBe(AttendanceStatus.Late);
        record.CountsAsAttended.ShouldBeTrue();
        record.RecordedAtUtc.ShouldBe(now.AddHours(1));
        Should.Throw<DomainException>(() => record.Amend((AttendanceStatus)9, Grader, now));
        Should.Throw<DomainException>(() => record.Amend(AttendanceStatus.Present, Guid.Empty, now));
    }
}
