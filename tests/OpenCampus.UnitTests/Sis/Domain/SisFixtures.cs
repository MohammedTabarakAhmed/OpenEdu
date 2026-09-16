using OpenCampus.Sis.Domain.Learners;
using OpenCampus.Sis.Domain.Sections;

namespace OpenCampus.UnitTests.Sis.Domain;

/// <summary>Builders for valid SIS aggregates; tests then vary one aspect to prove the refusal (TST-03).</summary>
internal static class SisFixtures
{
    public static readonly DateTime Now = new(2026, 9, 16, 9, 0, 0, DateTimeKind.Utc);
    public static readonly Guid InstructorId = Guid.NewGuid();

    public static CourseSection Section(int capacity = 2, SectionStatus status = SectionStatus.Open, Guid? instructorId = null)
    {
        var section = CourseSection.Create(
            Guid.NewGuid(), "cs101-a", "2026 Autumn",
            new DateOnly(2026, 9, 1), new DateOnly(2026, 12, 15),
            capacity, instructorId ?? InstructorId, DeliveryMode.InPerson);

        switch (status)
        {
            case SectionStatus.Open:
                section.Open();
                break;
            case SectionStatus.Closed:
                section.Open();
                section.Close();
                break;
            case SectionStatus.Cancelled:
                section.Cancel();
                break;
        }

        return section;
    }

    public static Learner Learner(LearnerStatus status = LearnerStatus.Active)
    {
        var learner = OpenCampus.Sis.Domain.Learners.Learner.Create(Guid.NewGuid(), "l-0001", null, new DateOnly(2004, 5, 1), Gender.Female, null);
        if (status != LearnerStatus.Active)
        {
            learner.ChangeStatus(status);
        }

        return learner;
    }
}
