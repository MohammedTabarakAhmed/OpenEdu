using OpenCampus.SharedKernel;

namespace OpenCampus.Lms.Application;

/// <summary>Application-level failures of the LMS module, classified for the host's status-code mapping (15.2).</summary>
public static class LmsErrors
{
    /// <summary>API-06: a section the caller is neither assigned to nor enrolled in is reported as absent, not forbidden.</summary>
    public static readonly Error SectionNotFound = Error.NotFound("sections.not_found", "The section was not found.");
    public static readonly Error ContentNotFound = Error.NotFound("content.not_found", "The content was not found.");
    public static readonly Error ContentItemNotFound = Error.NotFound("content_items.not_found", "The content item was not found.");
    public static readonly Error ResourceNotFound = Error.NotFound("resources.not_found", "The resource was not found.");
    public static readonly Error AssignmentNotFound = Error.NotFound("assignments.not_found", "The assignment was not found.");
    public static readonly Error SubmissionNotFound = Error.NotFound("submissions.not_found", "The submission was not found.");
    public static readonly Error SessionNotFound = Error.NotFound("sessions.not_found", "The session was not found.");

    public static Error LearnerNotEnrolled(Guid learnerUserId) =>
        Error.Validation("Entries", $"Learner {learnerUserId} holds no active enrolment in the section.");

    // SEC-22: upload controls; validation failures keyed to the file field (API-04).
    public static readonly Error UploadEmpty = Error.Validation("File", "The uploaded file is empty.");
    public static Error UploadExtensionNotPermitted(IEnumerable<string> permitted) =>
        Error.Validation("File", $"The file type is not permitted. Permitted extensions: {string.Join(", ", permitted)}.");
    public static Error UploadTooLarge(long maxBytes) =>
        Error.Validation("File", $"The file exceeds the maximum permitted size of {maxBytes} bytes.");
}

/// <summary>
/// Appendix C codes the LMS consults for resource-level scope (SEC-12). Administrative authority over a section
/// is expressed by the SIS capability to manage sections; the LMS cannot reference the Identity catalogue (MB-04),
/// so the code is restated here verbatim.
/// </summary>
public static class KnownPermissions
{
    public const string SectionAdministration = "sis.section.write";
}
