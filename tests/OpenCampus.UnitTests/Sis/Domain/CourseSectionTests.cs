using OpenCampus.SharedKernel;
using OpenCampus.Sis.Domain.Sections;
using static OpenCampus.UnitTests.Sis.Domain.SisFixtures;

namespace OpenCampus.UnitTests.Sis.Domain;

/// <summary>Section aggregate: lifecycle, BR-14, BR-16 and the grade-scheme invariants.</summary>
public class CourseSectionTests
{
    [Fact]
    public void Create_StartsInDraftWithNormalisedCode()
    {
        var section = Section(status: SectionStatus.Draft);

        section.Status.ShouldBe(SectionStatus.Draft);
        section.Code.ShouldBe("CS101-A");
        section.Sessions.ShouldBeEmpty();
        section.GradeComponents.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(CourseSection.MaxCapacity + 1)]
    public void Create_RejectsCapacityOutOfRange(int capacity)
    {
        Should.Throw<DomainException>(() => Section(capacity: capacity));
    }

    [Fact]
    public void Create_RejectsEndBeforeStartAndEmptyInstructor()
    {
        Should.Throw<DomainException>(() => CourseSection.Create(
            Guid.NewGuid(), "X", "T", new DateOnly(2026, 12, 1), new DateOnly(2026, 9, 1), 5, InstructorId, DeliveryMode.Online));
        Should.Throw<DomainException>(() => CourseSection.Create(
            Guid.NewGuid(), "X", "T", new DateOnly(2026, 9, 1), new DateOnly(2026, 12, 1), 5, Guid.Empty, DeliveryMode.Online));
    }

    [Fact]
    public void Transitions_FollowDraftOpenClosed()
    {
        var section = Section(status: SectionStatus.Draft);

        Should.Throw<DomainException>(section.Close);
        section.Open();
        section.Status.ShouldBe(SectionStatus.Open);
        Should.Throw<DomainException>(section.Open);
        section.Close();
        section.Status.ShouldBe(SectionStatus.Closed);
        Should.Throw<DomainException>(section.Cancel);
    }

    [Fact]
    public void Amend_RejectsCapacityBelowActiveEnrolments()
    {
        var section = Section(capacity: 5);

        Should.Throw<DomainException>(() =>
            section.Amend("T", section.StartDate, section.EndDate, 2, InstructorId, DeliveryMode.Online, activeEnrolmentCount: 3));

        section.Amend("T2", section.StartDate, section.EndDate, 3, InstructorId, DeliveryMode.Online, activeEnrolmentCount: 3);
        section.Capacity.ShouldBe(3);
        section.TermName.ShouldBe("T2");
    }

    [Fact]
    public void Amend_IsRefusedOnceClosed()
    {
        var section = Section(status: SectionStatus.Closed);

        Should.Throw<DomainException>(() =>
            section.Amend("T", section.StartDate, section.EndDate, 5, InstructorId, DeliveryMode.Online, 0));
    }

    // ----- BR-14 -----

    [Fact]
    public void BR14_EnsureDeletable_WhenAnyEnrolmentExists_IsRefused()
    {
        var section = Section();

        Should.Throw<BusinessRuleViolationException>(() => section.EnsureDeletable(enrolmentCount: 1))
            .RuleCode.ShouldBe("BR-14");
    }

    [Fact]
    public void BR14_EnsureDeletable_WithoutEnrolments_Succeeds()
    {
        Should.NotThrow(() => Section().EnsureDeletable(enrolmentCount: 0));
    }

    // ----- Sessions and BR-16 -----

    [Fact]
    public void AddSession_RejectsEndNotAfterStart()
    {
        var section = Section();

        Should.Throw<DomainException>(() => section.AddSession(Now, Now, null, []));
        Should.Throw<DomainException>(() => section.AddSession(Now, Now.AddMinutes(-1), null, []));
    }

    [Fact]
    public void BR16_AddSession_OverlappingOwnSession_IsRefused()
    {
        var section = Section();
        section.AddSession(Now, Now.AddHours(2), "Room 1", []);

        var violation = Should.Throw<BusinessRuleViolationException>(() =>
            section.AddSession(Now.AddHours(1), Now.AddHours(3), "Room 2", []));

        violation.RuleCode.ShouldBe("BR-16");
        section.Sessions.Count.ShouldBe(1);
    }

    [Fact]
    public void BR16_AddSession_OverlappingInstructorSessionInAnotherSection_IsRefused()
    {
        var other = Section();
        var elsewhere = other.AddSession(Now, Now.AddHours(1), null, []);
        var section = Section();

        Should.Throw<BusinessRuleViolationException>(() =>
                section.AddSession(Now.AddMinutes(30), Now.AddHours(2), null, [elsewhere]))
            .RuleCode.ShouldBe("BR-16");
    }

    [Fact]
    public void BR16_BackToBackSessions_DoNotOverlap()
    {
        var section = Section();
        section.AddSession(Now, Now.AddHours(1), null, []);

        Should.NotThrow(() => section.AddSession(Now.AddHours(1), Now.AddHours(2), null, []));
        section.Sessions.Count.ShouldBe(2);
    }

    [Fact]
    public void RescheduleSession_ExcludesItselfFromOverlapCheck()
    {
        var section = Section();
        var session = section.AddSession(Now, Now.AddHours(1), null, []);

        section.RescheduleSession(session.Id, Now.AddMinutes(15), Now.AddMinutes(75), "Lab", []);

        session.ScheduledStartUtc.ShouldBe(Now.AddMinutes(15));
        session.Location.ShouldBe("Lab");
    }

    [Fact]
    public void RemoveSession_UnknownId_IsNotFound()
    {
        Should.Throw<EntityNotFoundException>(() => Section().RemoveSession(Guid.NewGuid()));
    }

    // ----- Grade scheme -----

    [Fact]
    public void GradeScheme_WeightsMayNotExceedOneHundred()
    {
        var section = Section(status: SectionStatus.Draft);
        section.AddGradeComponent("Coursework", "أعمال", 60m, 100m);
        section.AddGradeComponent("Exam", "امتحان", 40m, 100m);

        section.TotalWeightPercent.ShouldBe(100m);
        Should.Throw<DomainException>(() => section.AddGradeComponent("Extra", "إضافي", 1m, 10m));
    }

    [Fact]
    public void GradeScheme_RejectsNonPositiveWeightOrMaxScore()
    {
        var section = Section(status: SectionStatus.Draft);

        Should.Throw<DomainException>(() => section.AddGradeComponent("A", "أ", 0m, 100m));
        Should.Throw<DomainException>(() => section.AddGradeComponent("A", "أ", 50m, 0m));
        Should.Throw<DomainException>(() => section.AddGradeComponent("A", "أ", 101m, 10m));
    }

    [Fact]
    public void GradeScheme_AmendAccountsForTheComponentBeingReplaced()
    {
        var section = Section(status: SectionStatus.Draft);
        var component = section.AddGradeComponent("Coursework", "أعمال", 60m, 100m);
        section.AddGradeComponent("Exam", "امتحان", 40m, 100m);

        section.AmendGradeComponent(component.Id, "Coursework", "أعمال", 50m, 50m);
        section.TotalWeightPercent.ShouldBe(90m);

        Should.Throw<DomainException>(() => section.AmendGradeComponent(component.Id, "Coursework", "أعمال", 61m, 50m));
    }

    [Fact]
    public void GradeScheme_IsFrozenOnceClosed()
    {
        var section = Section(status: SectionStatus.Closed);

        Should.Throw<DomainException>(() => section.AddGradeComponent("A", "أ", 10m, 10m));
    }
}
