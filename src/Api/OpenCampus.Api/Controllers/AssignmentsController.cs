using Microsoft.AspNetCore.Mvc;
using OpenCampus.Api.ErrorHandling;
using OpenCampus.Api.Security;
using OpenCampus.Identity.Application.Authorization;
using OpenCampus.Lms.Application.Assessment;
using OpenCampus.Lms.Application.Content;

namespace OpenCampus.Api.Controllers;

/// <summary>
/// Assignment publication and listing, submission creation and retrieval, submission marking (15.3 "Assessment
/// and grading"). Scope per section (SEC-12) is resolved by the application layer; anything outside it is
/// reported as 404 (API-06). BR-08 and BR-09 are enforced by the aggregate.
/// </summary>
[ApiController]
[Route("api/v1")]
[Produces("application/json")]
public sealed class AssignmentsController(AssignmentService assignments) : ControllerBase
{
    [HttpGet("sections/{sectionId:guid}/assignments")]
    [HasPermission(Permissions.Lms.AssignmentRead)]
    [ProducesResponseType<SectionAssignmentsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SectionAssignmentsResponse>> ListForSection(Guid sectionId, CancellationToken cancellationToken) =>
        (await assignments.ListForSectionAsync(sectionId, cancellationToken)).ToActionResult(this);

    [HttpPost("sections/{sectionId:guid}/assignments")]
    [HasPermission(Permissions.Lms.AssignmentWrite)]
    [ProducesResponseType<AssignmentResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AssignmentResponse>> Create(Guid sectionId, AssignmentRequest request, CancellationToken cancellationToken) =>
        (await assignments.CreateAsync(sectionId, request, cancellationToken)).ToCreatedResult(this, nameof(Get), r => new { id = r.Id });

    [HttpGet("assignments/{id:guid}")]
    [HasPermission(Permissions.Lms.AssignmentRead)]
    [ProducesResponseType<AssignmentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AssignmentResponse>> Get(Guid id, CancellationToken cancellationToken) =>
        (await assignments.GetAsync(id, cancellationToken)).ToActionResult(this);

    [HttpPut("assignments/{id:guid}")]
    [HasPermission(Permissions.Lms.AssignmentWrite)]
    [ProducesResponseType<AssignmentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AssignmentResponse>> Update(Guid id, AssignmentRequest request, CancellationToken cancellationToken) =>
        (await assignments.UpdateAsync(id, request, cancellationToken)).ToActionResult(this);

    [HttpDelete("assignments/{id:guid}")]
    [HasPermission(Permissions.Lms.AssignmentWrite)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        (await assignments.DeleteAsync(id, cancellationToken)).ToActionResult(this);

    [HttpPost("assignments/{id:guid}/publish")]
    [HasPermission(Permissions.Lms.AssignmentWrite)]
    [ProducesResponseType<AssignmentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AssignmentResponse>> Publish(Guid id, CancellationToken cancellationToken) =>
        (await assignments.PublishAsync(id, cancellationToken)).ToActionResult(this);

    [HttpPost("assignments/{id:guid}/unpublish")]
    [HasPermission(Permissions.Lms.AssignmentWrite)]
    [ProducesResponseType<AssignmentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AssignmentResponse>> Unpublish(Guid id, CancellationToken cancellationToken) =>
        (await assignments.UnpublishAsync(id, cancellationToken)).ToActionResult(this);

    /// <summary>Managers: every submission alongside the enrolled learners, so missing work is visible.</summary>
    [HttpGet("assignments/{id:guid}/submissions")]
    [HasPermission(Permissions.Lms.SubmissionRead)]
    [ProducesResponseType<AssignmentSubmissionsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AssignmentSubmissionsResponse>> ListSubmissions(Guid id, CancellationToken cancellationToken) =>
        (await assignments.ListSubmissionsAsync(id, cancellationToken)).ToActionResult(this);

    /// <summary>Learner submission (BR-08, BR-09); an optional file follows the upload controls of 16.5.</summary>
    [HttpPost("assignments/{id:guid}/submit")]
    [HasPermission(Permissions.Lms.SubmissionSubmit)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<SubmissionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<SubmissionResponse>> Submit(Guid id, [FromForm] string? textBody, IFormFile? file, CancellationToken cancellationToken)
    {
        FileUpload? upload = null;
        Stream? stream = null;
        if (file is not null)
        {
            stream = file.OpenReadStream();
            upload = new FileUpload(file.FileName, file.ContentType, file.Length, stream);
        }

        try
        {
            var result = await assignments.SubmitAsync(id, new SubmitWorkRequest(textBody), upload, cancellationToken);
            return result.IsFailure
                ? StatusCode(ProblemMapping.StatusCodeFor(result.Error.Type), result.Error.ToProblemDetails(HttpContext))
                : Ok(result.Value);
        }
        finally
        {
            if (stream is not null)
            {
                await stream.DisposeAsync();
            }
        }
    }

    [HttpPost("submissions/{id:guid}/mark")]
    [HasPermission(Permissions.Lms.SubmissionGrade)]
    [ProducesResponseType<SubmissionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SubmissionResponse>> Mark(Guid id, MarkSubmissionRequest request, CancellationToken cancellationToken) =>
        (await assignments.MarkAsync(id, request, cancellationToken)).ToActionResult(this);

    /// <summary>Managers of the section, or the learner who submitted it (SEC-12, SEC-25).</summary>
    [HttpGet("submissions/{id:guid}/file")]
    [HasPermission(Permissions.Lms.SubmissionRead)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadFile(Guid id, CancellationToken cancellationToken)
    {
        var result = await assignments.OpenSubmissionFileAsync(id, cancellationToken);
        if (result.IsFailure)
        {
            return StatusCode(ProblemMapping.StatusCodeFor(result.Error.Type), result.Error.ToProblemDetails(HttpContext));
        }

        var download = result.Value;
        return File(download.Content, download.ContentType, download.FileName, enableRangeProcessing: false);
    }
}
