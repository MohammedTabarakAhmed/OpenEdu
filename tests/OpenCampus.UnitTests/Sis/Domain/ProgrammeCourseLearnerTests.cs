using OpenCampus.SharedKernel;
using OpenCampus.Sis.Domain.Courses;
using OpenCampus.Sis.Domain.Learners;
using OpenCampus.Sis.Domain.Programmes;

namespace OpenCampus.UnitTests.Sis.Domain;

public class ProgrammeCourseLearnerTests
{
    [Fact]
    public void Programme_Create_NormalisesCodeAndStartsActive()
    {
        var programme = Programme.Create(" bsc-cs ", "Computer Science", "علوم الحاسوب", 36);

        programme.Code.ShouldBe("BSC-CS");
        programme.IsActive.ShouldBeTrue();
        programme.DurationMonths.ShouldBe(36);
    }

    [Theory]
    [InlineData("", "Name", "اسم", 12)]
    [InlineData("P1", "", "اسم", 12)]
    [InlineData("P1", "Name", "", 12)]
    [InlineData("P1", "Name", "اسم", 0)]
    public void Programme_Create_RejectsMissingValues(string code, string nameEn, string nameAr, int months)
    {
        Should.Throw<DomainException>(() => Programme.Create(code, nameEn, nameAr, months));
    }

    [Fact]
    public void Programme_Create_RejectsOverlongCode()
    {
        Should.Throw<DomainException>(() => Programme.Create(new string('P', Programme.CodeMaxLength + 1), "N", "ن", 1));
    }

    [Fact]
    public void Course_Create_RequiresProgrammeAndBoundsCredits()
    {
        Should.Throw<DomainException>(() => Course.Create(Guid.Empty, "C1", "N", "ن", null, null, 3));
        Should.Throw<DomainException>(() => Course.Create(Guid.NewGuid(), "C1", "N", "ن", null, null, -1));
        Should.Throw<DomainException>(() => Course.Create(Guid.NewGuid(), "C1", "N", "ن", null, null, Course.MaxCredits + 1));

        var course = Course.Create(Guid.NewGuid(), "cs101", "Intro", "مقدمة", "  ", null, 3);
        course.Code.ShouldBe("CS101");
        course.DescriptionEn.ShouldBeNull();
        course.Credits.ShouldBe(3);
    }

    [Fact]
    public void Learner_Create_RequiresUserAndNumber()
    {
        Should.Throw<DomainException>(() => Learner.Create(Guid.Empty, "L1", null, null, Gender.Unspecified, null));
        Should.Throw<DomainException>(() => Learner.Create(Guid.NewGuid(), " ", null, null, Gender.Unspecified, null));

        var learner = Learner.Create(Guid.NewGuid(), "l-100", "1234567890", new DateOnly(2003, 1, 1), Gender.Male, "+966500000000");
        learner.LearnerNumber.ShouldBe("L-100");
        learner.Status.ShouldBe(LearnerStatus.Active);
        learner.CanEnrol.ShouldBeTrue();
    }

    [Fact]
    public void Learner_ChangeStatus_RejectsUndefinedValue()
    {
        var learner = Learner.Create(Guid.NewGuid(), "L1", null, null, Gender.Unspecified, null);

        Should.Throw<DomainException>(() => learner.ChangeStatus((LearnerStatus)99));
        learner.ChangeStatus(LearnerStatus.Suspended);
        learner.CanEnrol.ShouldBeFalse();
    }
}
