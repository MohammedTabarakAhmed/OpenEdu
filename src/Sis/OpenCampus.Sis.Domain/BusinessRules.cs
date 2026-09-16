using OpenCampus.SharedKernel;

namespace OpenCampus.Sis.Domain;

/// <summary>The section 14 rules owned by the SIS aggregates, with their catalogued messages.</summary>
public static class BusinessRules
{
    public static BusinessRuleViolationException Br01SectionFull() =>
        new("BR-01", "Enrolment refused: the section has reached its capacity.");

    public static BusinessRuleViolationException Br02DuplicateEnrolment() =>
        new("BR-02", "Enrolment refused: the learner already holds an active enrolment in this section.");

    public static BusinessRuleViolationException Br03SectionNotOpen() =>
        new("BR-03", "Enrolment refused: the section is not open for enrolment.");

    public static BusinessRuleViolationException Br14SectionHasEnrolments() =>
        new("BR-14", "The section cannot be deleted while enrolments exist against it.");

    public static BusinessRuleViolationException Br16SessionOverlap() =>
        new("BR-16", "The session overlaps another session scheduled for the same instructor.");
}
