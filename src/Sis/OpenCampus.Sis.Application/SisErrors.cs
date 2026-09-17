using OpenCampus.SharedKernel;

namespace OpenCampus.Sis.Application;

/// <summary>Application-level failures of the SIS module, classified for the host's status-code mapping (15.2).</summary>
public static class SisErrors
{
    public static readonly Error ProgrammeNotFound = Error.NotFound("programmes.not_found", "The programme was not found.");
    public static readonly Error ProgrammeCodeTaken = Error.Conflict("programmes.code_taken", "The programme code is already in use.");
    public static readonly Error ProgrammeHasCourses = Error.Conflict("programmes.has_courses", "The programme cannot be deleted while courses belong to it.");

    public static readonly Error CourseNotFound = Error.NotFound("courses.not_found", "The course was not found.");
    public static readonly Error CourseCodeTaken = Error.Conflict("courses.code_taken", "The course code is already in use.");
    public static readonly Error CourseHasSections = Error.Conflict("courses.has_sections", "The course cannot be deleted while sections belong to it.");

    public static readonly Error SectionNotFound = Error.NotFound("sections.not_found", "The section was not found.");
    public static readonly Error SectionCodeTaken = Error.Conflict("sections.code_taken", "A section with this code already exists for the course.");
    public static readonly Error SessionNotFound = Error.NotFound("sessions.not_found", "The session was not found.");
    public static readonly Error GradeComponentNotFound = Error.NotFound("grade_components.not_found", "The grade component was not found.");

    public static readonly Error LearnerNotFound = Error.NotFound("learners.not_found", "The learner was not found.");
    public static readonly Error LearnerNumberTaken = Error.Conflict("learners.number_taken", "The learner number is already in use.");
    public static readonly Error LearnerUserAlreadyLinked = Error.Conflict("learners.user_linked", "A learner record already exists for this user.");
    public static readonly Error NoLearnerRecordForCaller = Error.NotFound("learners.none_for_caller", "No learner record is associated with the current user.");

    public static readonly Error EnrolmentNotFound = Error.NotFound("enrolments.not_found", "The enrolment was not found.");

    public static readonly Error CertificateNotFound = Error.NotFound("certificates.not_found", "The certificate was not found.");
    public static readonly Error CertificateDocumentMissing = Error.NotFound("certificates.document_missing", "The certificate document is not available.");

    public static Error UnknownUser(string field) => Error.Validation(field, "The referenced user does not exist or is not active.");

    public static Error UserLacksRole(string field, string role) => Error.Validation(field, $"The referenced user does not hold the {role} role.");
}
