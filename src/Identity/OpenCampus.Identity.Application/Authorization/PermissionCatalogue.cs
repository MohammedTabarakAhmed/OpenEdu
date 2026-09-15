namespace OpenCampus.Identity.Application.Authorization;

/// <summary>The four roles of SDD 2.3, provisioned as reference data (13.6).</summary>
public static class RoleNames
{
    public const string Administrator = "Administrator";
    public const string Registrar = "Registrar";
    public const string Instructor = "Instructor";
    public const string Learner = "Learner";

    public static readonly IReadOnlyDictionary<string, string> Descriptions = new Dictionary<string, string>
    {
        [Administrator] = "Full administrative authority over the catalogue, users, configuration and reporting",
        [Registrar] = "Restricted administrator responsible for applications, admissions and enrolment",
        [Instructor] = "Authors course content, sets and marks assessments, records attendance for assigned sections",
        [Learner] = "Enrols in courses, consumes content, submits work, views released results and certificates",
    };
}

/// <summary>
/// Permission codes of SDD Appendix C (module.resource.action). Named authorisation
/// policies are bound to these codes (SEC-11); a code grants capability, not scope (SEC-12).
/// </summary>
public static class Permissions
{
    public static class Identity
    {
        public const string UserRead = "identity.user.read";
        public const string UserWrite = "identity.user.write";
        public const string UserDeactivate = "identity.user.deactivate";
        public const string RoleAssign = "identity.role.assign";
        public const string SessionRevoke = "identity.session.revoke";
        public const string AuditRead = "identity.audit.read";
    }

    public static class Sis
    {
        public const string ProgrammeRead = "sis.programme.read";
        public const string ProgrammeWrite = "sis.programme.write";
        public const string CourseRead = "sis.course.read";
        public const string CourseWrite = "sis.course.write";
        public const string SectionRead = "sis.section.read";
        public const string SectionWrite = "sis.section.write";
        public const string SectionOpen = "sis.section.open";
        public const string LearnerRead = "sis.learner.read";
        public const string LearnerWrite = "sis.learner.write";
        public const string EnrolmentRead = "sis.enrolment.read";
        public const string EnrolmentWrite = "sis.enrolment.write";
        public const string GradeRead = "sis.grade.read";
        public const string GradeWrite = "sis.grade.write";
        public const string GradeRelease = "sis.grade.release";
        public const string CertificateIssue = "sis.certificate.issue";
        public const string ReportRead = "sis.report.read";
    }

    public static class Lms
    {
        public const string ContentRead = "lms.content.read";
        public const string ContentWrite = "lms.content.write";
        public const string ContentPublish = "lms.content.publish";
        public const string AssignmentRead = "lms.assignment.read";
        public const string AssignmentWrite = "lms.assignment.write";
        public const string SubmissionRead = "lms.submission.read";
        public const string SubmissionSubmit = "lms.submission.submit";
        public const string SubmissionGrade = "lms.submission.grade";
        public const string AttendanceRead = "lms.attendance.read";
        public const string AttendanceWrite = "lms.attendance.write";
        public const string AnnouncementWrite = "lms.announcement.write";
    }

    /// <summary>The complete catalogue with descriptions, in provisioning order.</summary>
    public static readonly IReadOnlyList<(string Code, string Description)> Catalogue =
    [
        (Identity.UserRead, "Read user accounts"),
        (Identity.UserWrite, "Create and amend user accounts"),
        (Identity.UserDeactivate, "Deactivate user accounts"),
        (Identity.RoleAssign, "Assign and remove roles"),
        (Identity.SessionRevoke, "Enumerate and revoke user sessions"),
        (Identity.AuditRead, "Read the security audit trail"),
        (Sis.ProgrammeRead, "Read programmes"),
        (Sis.ProgrammeWrite, "Create, amend and delete programmes"),
        (Sis.CourseRead, "Read courses"),
        (Sis.CourseWrite, "Create, amend and delete courses"),
        (Sis.SectionRead, "Read course sections"),
        (Sis.SectionWrite, "Create, amend and delete course sections"),
        (Sis.SectionOpen, "Transition a section to Open"),
        (Sis.LearnerRead, "Read learner records"),
        (Sis.LearnerWrite, "Create and amend learner records"),
        (Sis.EnrolmentRead, "Read enrolments"),
        (Sis.EnrolmentWrite, "Create and withdraw enrolments"),
        (Sis.GradeRead, "Read grade entries"),
        (Sis.GradeWrite, "Create and amend grade entries"),
        (Sis.GradeRelease, "Release grades for a section"),
        (Sis.CertificateIssue, "Issue certificates"),
        (Sis.ReportRead, "Read reports and transcripts"),
        (Lms.ContentRead, "Read course content"),
        (Lms.ContentWrite, "Author course content"),
        (Lms.ContentPublish, "Publish course content"),
        (Lms.AssignmentRead, "Read assignments"),
        (Lms.AssignmentWrite, "Create and amend assignments"),
        (Lms.SubmissionRead, "Read submissions"),
        (Lms.SubmissionSubmit, "Submit work"),
        (Lms.SubmissionGrade, "Mark submissions"),
        (Lms.AttendanceRead, "Read attendance"),
        (Lms.AttendanceWrite, "Record attendance"),
        (Lms.AnnouncementWrite, "Publish announcements"),
    ];

    public static IEnumerable<string> AllCodes => Catalogue.Select(p => p.Code);

    /// <summary>
    /// Default role mappings (Appendix C), derived from the role descriptions of SDD 2.3.
    /// Instructor and Learner capabilities are further constrained at resource level (SEC-12).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> DefaultRoleMappings =
        new Dictionary<string, IReadOnlyList<string>>
        {
            [RoleNames.Administrator] = [.. AllCodes],
            [RoleNames.Registrar] =
            [
                Sis.ProgrammeRead, Sis.CourseRead, Sis.SectionRead,
                Sis.LearnerRead, Sis.LearnerWrite,
                Sis.EnrolmentRead, Sis.EnrolmentWrite,
                Sis.ReportRead,
            ],
            [RoleNames.Instructor] =
            [
                Sis.CourseRead, Sis.SectionRead, Sis.EnrolmentRead,
                Sis.GradeRead, Sis.GradeWrite, Sis.GradeRelease,
                Lms.ContentRead, Lms.ContentWrite, Lms.ContentPublish,
                Lms.AssignmentRead, Lms.AssignmentWrite,
                Lms.SubmissionRead, Lms.SubmissionGrade,
                Lms.AttendanceRead, Lms.AttendanceWrite,
                Lms.AnnouncementWrite,
            ],
            [RoleNames.Learner] =
            [
                Sis.CourseRead, Sis.SectionRead,
                Sis.EnrolmentRead, Sis.EnrolmentWrite,
                Sis.GradeRead,
                Lms.ContentRead, Lms.AssignmentRead,
                Lms.SubmissionRead, Lms.SubmissionSubmit,
                Lms.AttendanceRead,
            ],
        };
}
