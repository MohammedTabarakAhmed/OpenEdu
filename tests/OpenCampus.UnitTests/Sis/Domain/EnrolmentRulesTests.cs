using OpenCampus.SharedKernel;
using OpenCampus.Sis.Domain.Enrolments;
using OpenCampus.Sis.Domain.Learners;
using OpenCampus.Sis.Domain.Sections;
using static OpenCampus.UnitTests.Sis.Domain.SisFixtures;

namespace OpenCampus.UnitTests.Sis.Domain;

/// <summary>Section 14 rules BR-01, BR-02 and BR-03: each proven by refusal of the violating case (TST-03).</summary>
public class EnrolmentRulesTests
{
    [Fact]
    public void Create_OnOpenSectionWithFreePlace_Succeeds()
    {
        var section = Section(capacity: 2);
        var learner = Learner();

        var enrolment = Enrolment.Create(learner, section, activeEnrolmentCount: 1, learnerHasActiveEnrolmentInSection: false, Now);

        enrolment.LearnerId.ShouldBe(learner.Id);
        enrolment.SectionId.ShouldBe(section.Id);
        enrolment.Status.ShouldBe(EnrolmentStatus.Active);
        enrolment.IsActive.ShouldBeTrue();
        enrolment.EnrolledAtUtc.ShouldBe(Now);
        enrolment.FinalGrade.ShouldBeNull();
    }

    [Fact]
    public void BR01_Create_WhenActiveEnrolmentsEqualCapacity_IsRefused()
    {
        var section = Section(capacity: 2);

        var violation = Should.Throw<BusinessRuleViolationException>(() =>
            Enrolment.Create(Learner(), section, activeEnrolmentCount: 2, learnerHasActiveEnrolmentInSection: false, Now));

        violation.RuleCode.ShouldBe("BR-01");
    }

    [Fact]
    public void BR01_Create_WhenActiveEnrolmentsExceedCapacity_IsRefused()
    {
        // Capacity may have been reduced administratively after over-subscription; the rule still holds.
        var section = Section(capacity: 1);

        Should.Throw<BusinessRuleViolationException>(() =>
                Enrolment.Create(Learner(), section, activeEnrolmentCount: 5, learnerHasActiveEnrolmentInSection: false, Now))
            .RuleCode.ShouldBe("BR-01");
    }

    [Fact]
    public void BR02_Create_WhenLearnerAlreadyHoldsActiveEnrolmentInSection_IsRefused()
    {
        var section = Section(capacity: 10);

        var violation = Should.Throw<BusinessRuleViolationException>(() =>
            Enrolment.Create(Learner(), section, activeEnrolmentCount: 1, learnerHasActiveEnrolmentInSection: true, Now));

        violation.RuleCode.ShouldBe("BR-02");
    }

    [Fact]
    public void BR02_Reinstate_AfterWithdrawal_IsPermitted()
    {
        // A withdrawn enrolment is not active, so re-enrolment (reinstating the same row) does not violate BR-02.
        var section = Section(capacity: 10);
        var learner = Learner();
        var enrolment = Enrolment.Create(learner, section, 0, false, Now);
        enrolment.Withdraw();
        enrolment.IsActive.ShouldBeFalse();

        enrolment.Reinstate(learner, section, 0, Now.AddDays(1));

        enrolment.IsActive.ShouldBeTrue();
        enrolment.EnrolledAtUtc.ShouldBe(Now.AddDays(1));
    }

    [Fact]
    public void BR02_Reinstate_WhileStillActive_IsRefused()
    {
        var section = Section(capacity: 10);
        var learner = Learner();
        var enrolment = Enrolment.Create(learner, section, 0, false, Now);

        Should.Throw<BusinessRuleViolationException>(() => enrolment.Reinstate(learner, section, 1, Now))
            .RuleCode.ShouldBe("BR-02");
    }

    [Fact]
    public void BR01_Reinstate_WhenSectionIsFull_IsRefused()
    {
        var section = Section(capacity: 1);
        var learner = Learner();
        var enrolment = Enrolment.Create(learner, section, 0, false, Now);
        enrolment.Withdraw();

        Should.Throw<BusinessRuleViolationException>(() => enrolment.Reinstate(learner, section, 1, Now))
            .RuleCode.ShouldBe("BR-01");
    }

    [Fact]
    public void Reinstate_ForDifferentSection_IsRefused()
    {
        var learner = Learner();
        var enrolment = Enrolment.Create(learner, Section(), 0, false, Now);
        enrolment.Withdraw();

        Should.Throw<DomainException>(() => enrolment.Reinstate(learner, Section(), 0, Now));
    }

    [Theory]
    [InlineData(SectionStatus.Draft)]
    [InlineData(SectionStatus.Closed)]
    [InlineData(SectionStatus.Cancelled)]
    public void BR03_Create_WhenSectionIsNotOpen_IsRefused(SectionStatus status)
    {
        var section = Section(capacity: 10, status: status);

        var violation = Should.Throw<BusinessRuleViolationException>(() =>
            Enrolment.Create(Learner(), section, activeEnrolmentCount: 0, learnerHasActiveEnrolmentInSection: false, Now));

        violation.RuleCode.ShouldBe("BR-03");
    }

    [Fact]
    public void BR03_TakesPrecedenceOverBR01_WhenSectionIsNotOpenAndFull()
    {
        var section = Section(capacity: 1, status: SectionStatus.Draft);

        Should.Throw<BusinessRuleViolationException>(() => section.EnsureAcceptsEnrolment(activeEnrolmentCount: 1))
            .RuleCode.ShouldBe("BR-03");
    }

    [Theory]
    [InlineData(LearnerStatus.Suspended)]
    [InlineData(LearnerStatus.Graduated)]
    [InlineData(LearnerStatus.Withdrawn)]
    public void Create_WhenLearnerIsNotActive_IsRefused(LearnerStatus status)
    {
        Should.Throw<DomainException>(() => Enrolment.Create(Learner(status), Section(), 0, false, Now));
    }

    [Fact]
    public void Withdraw_TwiceIsRefused()
    {
        var enrolment = Enrolment.Create(Learner(), Section(), 0, false, Now);
        enrolment.Withdraw();

        enrolment.Status.ShouldBe(EnrolmentStatus.Withdrawn);
        Should.Throw<DomainException>(enrolment.Withdraw);
    }
}
