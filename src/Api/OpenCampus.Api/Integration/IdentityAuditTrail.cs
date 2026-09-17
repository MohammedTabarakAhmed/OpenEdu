using OpenCampus.Identity.Application.Abstractions;
using OpenCampus.Identity.Domain.Audit;
using OpenCampus.Sis.Application.Abstractions;

namespace OpenCampus.Api.Integration;

/// <summary>
/// Implements the SIS→Identity audit contract (6.5, MB-02) at the composition root (MB-05): writes an immutable
/// audit record through Identity's published repository (SEC-30), capturing actor, event type, affected entity,
/// timestamp and originating address (SEC-31). Committed separately from the SIS change, after it succeeded.
/// </summary>
public sealed class IdentityAuditTrail(
    IAuditEventRepository events,
    IIdentityUnitOfWork unitOfWork,
    IHttpContextAccessor accessor,
    TimeProvider clock) : IAuditTrail
{
    public Task RecordAsync(string eventType, string entityName, Guid entityId, string? detailsJson, CancellationToken cancellationToken)
    {
        var context = accessor.HttpContext;
        var userId = Guid.TryParse(context?.User.FindFirst(Microsoft.IdentityModel.JsonWebTokens.JwtRegisteredClaimNames.Sub)?.Value, out var id) ? id : (Guid?)null;
        events.Add(AuditEvent.Record(eventType, entityName, entityId, userId, clock.GetUtcNow().UtcDateTime, context?.Connection.RemoteIpAddress?.ToString(), detailsJson));
        return unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
