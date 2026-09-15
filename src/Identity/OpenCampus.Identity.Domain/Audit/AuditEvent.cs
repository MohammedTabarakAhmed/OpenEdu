using OpenCampus.SharedKernel;

namespace OpenCampus.Identity.Domain.Audit;

/// <summary>
/// Immutable audit record (SEC-30 to SEC-32). Deliberately not derived from
/// <see cref="Entity"/>: it carries no modification or deletion state because it
/// is never modified or deleted through any application interface.
/// </summary>
public sealed class AuditEvent
{
    public const int EventTypeMaxLength = 64;
    public const int EntityNameMaxLength = 128;
    public const int DetailsMaxLength = 4000;
    public const int IpAddressMaxLength = 45;

    private AuditEvent()
    {
    }

    public Guid Id { get; private set; }

    /// <summary>The actor; null for anonymous events such as a failed authentication for an unknown principal.</summary>
    public Guid? UserId { get; private set; }

    public string EventType { get; private set; } = null!;

    public string EntityName { get; private set; } = null!;

    public Guid? EntityId { get; private set; }

    public string? DetailsJson { get; private set; }

    public DateTime OccurredAtUtc { get; private set; }

    public string? IpAddress { get; private set; }

    public static AuditEvent Record(
        string eventType,
        string entityName,
        Guid? entityId,
        Guid? userId,
        DateTime occurredAtUtc,
        string? ipAddress,
        string? detailsJson = null)
    {
        if (string.IsNullOrWhiteSpace(eventType))
        {
            throw new DomainException("Audit event type is required.");
        }

        if (string.IsNullOrWhiteSpace(entityName))
        {
            throw new DomainException("Audit entity name is required.");
        }

        return new AuditEvent
        {
            Id = SequentialGuid.NewGuid(),
            EventType = eventType,
            EntityName = entityName,
            EntityId = entityId,
            UserId = userId,
            OccurredAtUtc = occurredAtUtc,
            IpAddress = ipAddress is { Length: > IpAddressMaxLength } ? ipAddress[..IpAddressMaxLength] : ipAddress,
            DetailsJson = detailsJson,
        };
    }
}
