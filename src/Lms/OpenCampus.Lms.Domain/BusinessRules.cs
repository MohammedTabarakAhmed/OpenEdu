using OpenCampus.SharedKernel;

namespace OpenCampus.Lms.Domain;

/// <summary>The section 14 rules owned by the LMS aggregates, with their catalogued messages.</summary>
public static class BusinessRules
{
    public static BusinessRuleViolationException Br08LateSubmissionDisallowed() =>
        new("BR-08", "The submission is after the due date and late submission is not allowed for this assignment.");

    public static BusinessRuleViolationException Br09NoActiveEnrolment() =>
        new("BR-09", "The learner holds no active enrolment in the assignment's section.");

    public static BusinessRuleViolationException Br13SessionInFuture() =>
        new("BR-13", "Attendance cannot be recorded for a session scheduled in the future.");

    public static BusinessRuleViolationException Br15ContentNotPublished() =>
        new("BR-15", "The content item is not visible to learners until it is published.");
}
