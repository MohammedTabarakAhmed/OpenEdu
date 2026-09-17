namespace OpenCampus.Sis.Application.Certificates;

/// <summary>A certificate as seen by its learner or an administrator: identifiers plus the display data of the enrolment behind it.</summary>
public sealed record CertificateResponse(
    Guid Id,
    Guid EnrolmentId,
    Guid LearnerId,
    string LearnerNumber,
    string LearnerFullNameEn,
    string LearnerFullNameAr,
    Guid SectionId,
    string SectionCode,
    string Term,
    string CourseCode,
    string CourseNameEn,
    string CourseNameAr,
    string ProgrammeCode,
    string ProgrammeNameEn,
    string ProgrammeNameAr,
    decimal FinalGrade,
    DateTime CompletedAtUtc,
    string VerificationCode,
    DateTime IssuedAtUtc);

/// <summary>
/// The anonymous verification answer (15.4). Deliberately minimal: it confirms that a certificate with the given code
/// exists and names what it certifies, without exposing internal identifiers or the learner number.
/// </summary>
public sealed record CertificateVerificationResponse(
    string VerificationCode,
    DateTime IssuedAtUtc,
    string LearnerFullNameEn,
    string LearnerFullNameAr,
    string CourseCode,
    string CourseNameEn,
    string CourseNameAr,
    string ProgrammeNameEn,
    string ProgrammeNameAr,
    string Term,
    DateTime CompletedAtUtc);

/// <summary>An opened certificate document for download (SEC-25): the stream, its media type and the file name to suggest.</summary>
public sealed record CertificateDownload(Stream Content, string ContentType, string FileName);
