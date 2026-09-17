using Microsoft.AspNetCore.Mvc;
using OpenCampus.Api.Security;
using OpenCampus.Identity.Application.Authorization;
using OpenCampus.Sis.Application.Reports;
using OpenCampus.Sis.Domain.Sections;

namespace OpenCampus.Api.Controllers;

/// <summary>
/// Operational reporting on participation and attainment (BO-04; Increment 6 "reporting"), under `sis.report.read`
/// (Administrator and Registrar by default). Both reports accept the same optional filter and are computed on request.
/// </summary>
[ApiController]
[Route("api/v1/reports")]
[Produces("application/json")]
public sealed class ReportsController(ReportingService reports) : ControllerBase
{
    /// <summary>Enrolment standing per section against capacity.</summary>
    [HttpGet("participation")]
    [HasPermission(Permissions.Sis.ReportRead)]
    [ProducesResponseType<ParticipationReport>(StatusCodes.Status200OK)]
    public Task<ParticipationReport> Participation(
        [FromQuery] Guid? programmeId, [FromQuery] Guid? courseId, [FromQuery] string? term, [FromQuery] SectionStatus? status, CancellationToken cancellationToken) =>
        reports.ParticipationAsync(new ReportFilter(programmeId, courseId, term, status), cancellationToken);

    /// <summary>Outcomes of completed enrolments per section: pass rate against the configured threshold, grade spread, certificates issued.</summary>
    [HttpGet("attainment")]
    [HasPermission(Permissions.Sis.ReportRead)]
    [ProducesResponseType<AttainmentReport>(StatusCodes.Status200OK)]
    public Task<AttainmentReport> Attainment(
        [FromQuery] Guid? programmeId, [FromQuery] Guid? courseId, [FromQuery] string? term, [FromQuery] SectionStatus? status, CancellationToken cancellationToken) =>
        reports.AttainmentAsync(new ReportFilter(programmeId, courseId, term, status), cancellationToken);
}
