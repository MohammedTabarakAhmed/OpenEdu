using Microsoft.AspNetCore.Mvc;
using OpenCampus.Api.ErrorHandling;
using OpenCampus.Api.Security;
using OpenCampus.Identity.Application.Authorization;
using OpenCampus.Sis.Application.Grading;

namespace OpenCampus.Api.Controllers;

/// <summary>
/// Section gradebook, grade recording and grade release (15.3 "Assessment and grading"). Scope (SEC-12) is
/// resolved by the application layer: the section's assigned instructor or a holder of section administration;
/// everyone else is answered "not found" (API-06). BR-05 and BR-07 are enforced by the aggregates.
/// </summary>
[ApiController]
[Route("api/v1/sections/{sectionId:guid}/grades")]
[Produces("application/json")]
public sealed class GradingController(GradingService grading) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Sis.GradeRead)]
    [ProducesResponseType<GradebookResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GradebookResponse>> GetGradebook(Guid sectionId, CancellationToken cancellationToken) =>
        (await grading.GetGradebookAsync(sectionId, cancellationToken)).ToActionResult(this);

    [HttpPost]
    [HasPermission(Permissions.Sis.GradeWrite)]
    [ProducesResponseType<GradeEntryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<GradeEntryResponse>> RecordGrade(Guid sectionId, RecordGradeRequest request, CancellationToken cancellationToken) =>
        (await grading.RecordGradeAsync(sectionId, request, cancellationToken)).ToActionResult(this);

    /// <summary>BR-07 via the aggregate; enrolments complete with their weighted final grade.</summary>
    [HttpPost("release")]
    [HasPermission(Permissions.Sis.GradeRelease)]
    [ProducesResponseType<GradebookResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<GradebookResponse>> Release(Guid sectionId, CancellationToken cancellationToken) =>
        (await grading.ReleaseAsync(sectionId, cancellationToken)).ToActionResult(this);
}
