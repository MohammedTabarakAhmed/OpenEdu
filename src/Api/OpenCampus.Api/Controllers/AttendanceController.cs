using Microsoft.AspNetCore.Mvc;
using OpenCampus.Api.ErrorHandling;
using OpenCampus.Api.Security;
using OpenCampus.Identity.Application.Authorization;
using OpenCampus.Lms.Application.Assessment;

namespace OpenCampus.Api.Controllers;

/// <summary>
/// Attendance recording and retrieval (15.3). BR-13 is decided by the aggregate from the session's scheduled
/// start; each register update reports the affected learners' rates to SIS for BR-12. Scope per section
/// (SEC-12): managers record and see everyone; a learner sees only their own records.
/// </summary>
[ApiController]
[Route("api/v1/sections/{sectionId:guid}")]
[Produces("application/json")]
public sealed class AttendanceController(AttendanceService attendance) : ControllerBase
{
    [HttpGet("attendance")]
    [HasPermission(Permissions.Lms.AttendanceRead)]
    [ProducesResponseType<SectionAttendanceResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SectionAttendanceResponse>> GetForSection(Guid sectionId, CancellationToken cancellationToken) =>
        (await attendance.GetForSectionAsync(sectionId, cancellationToken)).ToActionResult(this);

    [HttpGet("sessions/{sessionId:guid}/attendance")]
    [HasPermission(Permissions.Lms.AttendanceRead)]
    [ProducesResponseType<SessionRegisterResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SessionRegisterResponse>> GetRegister(Guid sectionId, Guid sessionId, CancellationToken cancellationToken) =>
        (await attendance.GetRegisterAsync(sectionId, sessionId, cancellationToken)).ToActionResult(this);

    /// <summary>BR-13 in the aggregate; an unenrolled learner is refused as a field-keyed 400.</summary>
    [HttpPost("sessions/{sessionId:guid}/attendance")]
    [HasPermission(Permissions.Lms.AttendanceWrite)]
    [ProducesResponseType<SessionRegisterResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<SessionRegisterResponse>> Record(Guid sectionId, Guid sessionId, RecordAttendanceRequest request, CancellationToken cancellationToken) =>
        (await attendance.RecordAsync(sectionId, sessionId, request, cancellationToken)).ToActionResult(this);
}
