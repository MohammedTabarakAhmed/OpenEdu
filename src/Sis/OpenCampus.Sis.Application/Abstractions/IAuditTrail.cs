namespace OpenCampus.Sis.Application.Abstractions;

/// <summary>
/// Cross-module contract to the Identity module (6.5: "record security-relevant events in the audit trail").
/// SEC-30 requires an immutable record for grade creation and amendment and for grade release; SIS declares the
/// need here (MB-02) and the host writes the event through Identity's published contract (MB-05), capturing
/// actor, timestamp and originating address (SEC-31).
/// </summary>
public interface IAuditTrail
{
    Task RecordAsync(string eventType, string entityName, Guid entityId, string? detailsJson, CancellationToken cancellationToken);
}

/// <summary>SEC-30 event types raised by the SIS module.</summary>
public static class SisAuditEventTypes
{
    public const string GradeCreated = "grade.created";
    public const string GradeAmended = "grade.amended";
    public const string GradesReleased = "grade.released";
    public const string CertificateIssued = "certificate.issued";
}
