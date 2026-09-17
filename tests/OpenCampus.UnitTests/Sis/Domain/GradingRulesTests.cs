using OpenCampus.SharedKernel;
using OpenCampus.Sis.Domain.Enrolments;
using OpenCampus.Sis.Domain.Sections;
using static OpenCampus.UnitTests.Sis.Domain.SisFixtures;

namespace OpenCampus.UnitTests.Sis.Domain;

/// <summary>Grading rules owned by the SIS aggregates: BR-04 (section), BR-05 (grade entry), BR-06 and BR-12 (enrolment), BR-07 (section).</summary>
public class GradingRulesTests
{
    private static readonly Guid Grader = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc);

    private static (Enrolment Enrolment, CourseSection Section) ActiveEnrolment()
    {
        var section = Section(capacity: 5);
        var enrolment = Enrolment.Create(Learner(), section, 0, false, Now);
        return (enrolment, section);
    }

    // ----- BR-04: a section opens only with weightings totalling exactly 100 -----

    [Theory]
    [InlineData(0)]
    [InlineData(60)]
    [InlineData(99.99)]
    public void BR04_Open_IsRefusedWhenWeightingsDoNotTotal100(decimal weight)
    {
        var section = CourseSection.Create(Guid.NewGuid(), "X", "T", new DateOnly(2026, 9, 1), new DateOnly(2026, 12, 1), 5, InstructorId, DeliveryMode.Online);
        if (weight > 0)
        {
            section.AddGradeComponent("Only", "فقط", weight, 100m);
        }

        var violation = Should.Throw<BusinessRuleViolationException>(section.Open);

        violation.RuleCode.ShouldBe("BR-04");
        section.Status.ShouldBe(SectionStatus.Draft);
    }

    [Fact]
    public void BR04_Open_SucceedsAtExactly100()
    {
        var section = CourseSection.Create(Guid.NewGuid(), "X", "T", new DateOnly(2026, 9, 1), new DateOnly(2026, 12, 1), 5, InstructorId, DeliveryMode.Online);
        section.AddGradeComponent("A", "أ", 33.33m, 100m);
        section.AddGradeComponent("B", "ب", 33.33m, 100m);
        section.AddGradeComponent("C", "ج", 33.34m, 100m);

        section.Open();

        section.Status.ShouldBe(SectionStatus.Open);
    }

    // ----- BR-05: a score is neither negative nor above the component maximum -----

    [Theory]
    [InlineData(-0.01)]
    [InlineData(100.01)]
    [InlineData(500)]
    public void BR05_ScoreOutOfRange_IsRefused(decimal score)
    {
        var (enrolment, section) = ActiveEnrolment();
        var component = section.GradeComponents.First(); // max 100

        var violation = Should.Throw<BusinessRuleViolationException>(() => GradeEntry.Create(enrolment, component, score, Grader, Now));

        violation.RuleCode.ShouldBe("BR-05");
    }

    [Fact]
    public void BR05_BoundaryScores_AreAccepted_AndAmendmentIsBoundedToo()
    {
        var (enrolment, section) = ActiveEnrolment();
        var component = section.GradeComponents.Last(); // max 50

        var entry = GradeEntry.Create(enrolment, component, 0m, Grader, Now);
        entry.Amend(component, 50m, Grader, Now.AddDays(1));
        entry.Score.ShouldBe(50m);
        entry.GradedByUserId.ShouldBe(Grader);
        entry.IsReleased.ShouldBeFalse();

        Should.Throw<BusinessRuleViolationException>(() => entry.Amend(component, 50.5m, Grader, Now)).RuleCode.ShouldBe("BR-05");
        Should.Throw<DomainException>(() => entry.Amend(section.GradeComponents.First(), 10m, Grader, Now)); // wrong component
    }

    [Fact]
    public void GradeEntry_RequiresActiveEnrolment_AndComponentOfTheSameSection()
    {
        var (enrolment, section) = ActiveEnrolment();
        var otherSection = Section(capacity: 5);
        Should.Throw<DomainException>(() => GradeEntry.Create(enrolment, otherSection.GradeComponents.First(), 10m, Grader, Now));

        enrolment.Withdraw();
        Should.Throw<DomainException>(() => GradeEntry.Create(enrolment, section.GradeComponents.First(), 10m, Grader, Now));
    }

    [Fact]
    public void ReleasedGradeEntry_CannotBeAmended()
    {
        var (enrolment, section) = ActiveEnrolment();
        var component = section.GradeComponents.First();
        var entry = GradeEntry.Create(enrolment, component, 70m, Grader, Now);

        entry.Release();

        entry.IsReleased.ShouldBeTrue();
        Should.Throw<DomainException>(() => entry.Amend(component, 80m, Grader, Now));
    }

    // ----- BR-06: grade entries are not visible to a learner until released -----

    [Fact]
    public void BR06_UnreleasedGrade_IsRefusedToLearner_AndFilteredFromTheirView()
    {
        var (enrolment, section) = ActiveEnrolment();
        var unreleased = GradeEntry.Create(enrolment, section.GradeComponents.First(), 70m, Grader, Now);
        var released = GradeEntry.Create(enrolment, section.GradeComponents.Last(), 40m, Grader, Now);
        released.Release();

        Should.Throw<BusinessRuleViolationException>(() => enrolment.EnsureGradeVisibleToLearner(unreleased)).RuleCode.ShouldBe("BR-06");
        Should.NotThrow(() => enrolment.EnsureGradeVisibleToLearner(released));
        enrolment.GradesVisibleToLearner([unreleased, released]).ShouldBe([released]);
    }

    [Fact]
    public void BR06_AnotherEnrolmentsGrade_IsNeverVisible()
    {
        var (mine, section) = ActiveEnrolment();
        var theirs = Enrolment.Create(Learner(), section, 1, false, Now);
        var theirGrade = GradeEntry.Create(theirs, section.GradeComponents.First(), 90m, Grader, Now);
        theirGrade.Release();

        mine.GradesVisibleToLearner([theirGrade]).ShouldBeEmpty();
        Should.Throw<DomainException>(() => mine.EnsureGradeVisibleToLearner(theirGrade));
    }

    // ----- BR-07: release is refused while any component is ungraded for an active enrolment -----

    [Fact]
    public void BR07_Release_IsRefusedWithUngradedPairs()
    {
        var section = Section(capacity: 5);

        Should.Throw<BusinessRuleViolationException>(() => section.EnsureGradesReleasable(ungradedPairCount: 1)).RuleCode.ShouldBe("BR-07");
        Should.NotThrow(() => section.EnsureGradesReleasable(ungradedPairCount: 0));
    }

    [Fact]
    public void BR07_Release_RequiresOpenOrClosedSectionWithAScheme()
    {
        var draft = Section(status: SectionStatus.Draft);
        Should.Throw<DomainException>(() => draft.EnsureGradesReleasable(0));

        var closed = Section(status: SectionStatus.Closed);
        Should.NotThrow(() => closed.EnsureGradesReleasable(0));

        var cancelled = Section(status: SectionStatus.Cancelled);
        Should.Throw<DomainException>(() => cancelled.EnsureGradesReleasable(0));
    }

    [Fact]
    public void FinalGrade_IsTheWeightedSumOnA100Scale()
    {
        var section = Section(); // Coursework 60% of 100, Final 40% of 50
        var coursework = section.GradeComponents.First();
        var final = section.GradeComponents.Last();

        // 80/100 × 60 + 25/50 × 40 = 48 + 20 = 68
        section.ComputeFinalGrade(new Dictionary<Guid, decimal> { [coursework.Id] = 80m, [final.Id] = 25m }).ShouldBe(68m);
        Should.Throw<DomainException>(() => section.ComputeFinalGrade(new Dictionary<Guid, decimal> { [coursework.Id] = 80m }));
    }

    // ----- Completion -----

    [Fact]
    public void Complete_RecordsFinalGradeAndStatus_OnlyForActiveEnrolments()
    {
        var (enrolment, _) = ActiveEnrolment();

        enrolment.Complete(68m, Now);

        enrolment.Status.ShouldBe(EnrolmentStatus.Completed);
        enrolment.FinalGrade.ShouldBe(68m);
        enrolment.CompletedAtUtc.ShouldBe(Now);
        Should.Throw<DomainException>(() => enrolment.Complete(70m, Now));
        Should.Throw<DomainException>(enrolment.Withdraw);

        var (other, _) = ActiveEnrolment();
        Should.Throw<DomainException>(() => other.Complete(101m, Now));
    }

    // ----- BR-12: attendance below the threshold places the enrolment At Risk -----

    [Fact]
    public void BR12_AttendanceBelowThreshold_PlacesEnrolmentAtRisk_AndRecoveryRestoresActive()
    {
        var (enrolment, _) = ActiveEnrolment();

        enrolment.ApplyAttendanceRate(attendancePercent: 60m, thresholdPercent: 75m);
        enrolment.Status.ShouldBe(EnrolmentStatus.AtRisk);
        enrolment.IsActive.ShouldBeTrue(); // still counts against capacity and may be graded

        enrolment.ApplyAttendanceRate(75m, 75m);
        enrolment.Status.ShouldBe(EnrolmentStatus.Active);
    }

    [Fact]
    public void BR12_DoesNotTouchWithdrawnOrCompletedEnrolments_AndRejectsBadPercentages()
    {
        var (withdrawn, _) = ActiveEnrolment();
        withdrawn.Withdraw();
        withdrawn.ApplyAttendanceRate(0m, 75m);
        withdrawn.Status.ShouldBe(EnrolmentStatus.Withdrawn);

        var (completed, _) = ActiveEnrolment();
        completed.Complete(80m, Now);
        completed.ApplyAttendanceRate(0m, 75m);
        completed.Status.ShouldBe(EnrolmentStatus.Completed);

        var (active, _) = ActiveEnrolment();
        Should.Throw<DomainException>(() => active.ApplyAttendanceRate(101m, 75m));
        Should.Throw<DomainException>(() => active.ApplyAttendanceRate(50m, -1m));
    }
}
