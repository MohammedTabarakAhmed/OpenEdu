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

    public static BusinessRuleViolationException Br04WeightingsNotComplete(decimal total) =>
        new("BR-04", $"The section cannot be opened: its grade component weightings total {total} percent, not 100.");

    public static BusinessRuleViolationException Br05ScoreOutOfRange(decimal maxScore) =>
        new("BR-05", $"The score must be between 0 and the component maximum of {maxScore}.");

    public static BusinessRuleViolationException Br06GradesNotReleased() =>
        new("BR-06", "Grade entries are not visible until they are released for the section.");

    public static BusinessRuleViolationException Br07UngradedComponents() =>
        new("BR-07", "Grades cannot be released while a component remains ungraded for an active enrolment.");

    public static BusinessRuleViolationException Br14SectionHasEnrolments() =>
        new("BR-14", "The section cannot be deleted while enrolments exist against it.");

    public static BusinessRuleViolationException Br16SessionOverlap() =>
        new("BR-16", "The session overlaps another session scheduled for the same instructor.");

    public static BusinessRuleViolationException Br10CertificateNotEligible(decimal passThresholdPercent) =>
        new("BR-10", $"A certificate can only be issued for a completed enrolment with a final grade at or above the pass threshold of {passThresholdPercent} percent.");

    public static BusinessRuleViolationException Br11CertificateAlreadyIssued() =>
        new("BR-11", "A certificate has already been issued for this enrolment.");
}
