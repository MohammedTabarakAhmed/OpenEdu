using Microsoft.AspNetCore.Mvc;
using OpenCampus.Api.ErrorHandling;
using OpenCampus.Api.Security;
using OpenCampus.Identity.Application.Authorization;
using OpenCampus.Lms.Application.Abstractions;
using OpenCampus.Lms.Application.Content;

namespace OpenCampus.Api.Controllers;

/// <summary>
/// Content delivery capability (15.3): content hierarchy management, content item management and
/// publication, resource upload. Permission policies grant the capability (SEC-11); the application layer
/// establishes scope per section (SEC-12) and reports anything outside it as 404 (API-06).
/// </summary>
[ApiController]
[Route("api/v1")]
[Produces("application/json")]
public sealed class ContentController(ContentService content) : ControllerBase
{
    // ----- A section's hierarchy -----

    /// <summary>Managers receive every item; enrolled learners only published ones (BR-15).</summary>
    [HttpGet("sections/{sectionId:guid}/content")]
    [HasPermission(Permissions.Lms.ContentRead)]
    [ProducesResponseType<SectionContentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SectionContentResponse>> GetSectionContent(Guid sectionId, CancellationToken cancellationToken) =>
        (await content.GetSectionContentAsync(sectionId, cancellationToken)).ToActionResult(this);

    [HttpPost("sections/{sectionId:guid}/content")]
    [HasPermission(Permissions.Lms.ContentWrite)]
    [ProducesResponseType<CourseContentResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CourseContentResponse>> CreateContent(Guid sectionId, CreateCourseContentRequest request, CancellationToken cancellationToken) =>
        (await content.CreateContentAsync(sectionId, request, cancellationToken)).ToCreatedResult(this, nameof(GetContent), r => new { id = r.Id });

    // ----- Content units -----

    [HttpGet("content/{id:guid}")]
    [HasPermission(Permissions.Lms.ContentRead)]
    [ProducesResponseType<CourseContentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CourseContentResponse>> GetContent(Guid id, CancellationToken cancellationToken) =>
        (await content.GetContentAsync(id, cancellationToken)).ToActionResult(this);

    [HttpPut("content/{id:guid}")]
    [HasPermission(Permissions.Lms.ContentWrite)]
    [ProducesResponseType<CourseContentResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<CourseContentResponse>> UpdateContent(Guid id, UpdateCourseContentRequest request, CancellationToken cancellationToken) =>
        (await content.UpdateContentAsync(id, request, cancellationToken)).ToActionResult(this);

    [HttpDelete("content/{id:guid}")]
    [HasPermission(Permissions.Lms.ContentWrite)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteContent(Guid id, CancellationToken cancellationToken) =>
        (await content.DeleteContentAsync(id, cancellationToken)).ToActionResult(this);

    // ----- Items -----

    [HttpPost("content/{id:guid}/items")]
    [HasPermission(Permissions.Lms.ContentWrite)]
    [ProducesResponseType<ContentItemResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<ContentItemResponse>> AddItem(Guid id, CreateContentItemRequest request, CancellationToken cancellationToken) =>
        (await content.AddItemAsync(id, request, cancellationToken)).ToCreatedResult(this, nameof(GetContent), _ => new { id });

    [HttpPut("content/{id:guid}/items/reorder")]
    [HasPermission(Permissions.Lms.ContentWrite)]
    [ProducesResponseType<CourseContentResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<CourseContentResponse>> ReorderItems(Guid id, ReorderItemsRequest request, CancellationToken cancellationToken) =>
        (await content.ReorderItemsAsync(id, request, cancellationToken)).ToActionResult(this);

    [HttpPut("content/{id:guid}/items/{itemId:guid}")]
    [HasPermission(Permissions.Lms.ContentWrite)]
    [ProducesResponseType<ContentItemResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ContentItemResponse>> UpdateItem(Guid id, Guid itemId, UpdateContentItemRequest request, CancellationToken cancellationToken) =>
        (await content.UpdateItemAsync(id, itemId, request, cancellationToken)).ToActionResult(this);

    [HttpDelete("content/{id:guid}/items/{itemId:guid}")]
    [HasPermission(Permissions.Lms.ContentWrite)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveItem(Guid id, Guid itemId, CancellationToken cancellationToken) =>
        (await content.RemoveItemAsync(id, itemId, cancellationToken)).ToActionResult(this);

    [HttpPost("content/{id:guid}/items/{itemId:guid}/publish")]
    [HasPermission(Permissions.Lms.ContentPublish)]
    [ProducesResponseType<ContentItemResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ContentItemResponse>> PublishItem(Guid id, Guid itemId, CancellationToken cancellationToken) =>
        (await content.PublishItemAsync(id, itemId, cancellationToken)).ToActionResult(this);

    [HttpPost("content/{id:guid}/items/{itemId:guid}/unpublish")]
    [HasPermission(Permissions.Lms.ContentPublish)]
    [ProducesResponseType<ContentItemResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<ContentItemResponse>> UnpublishItem(Guid id, Guid itemId, CancellationToken cancellationToken) =>
        (await content.UnpublishItemAsync(id, itemId, cancellationToken)).ToActionResult(this);

    // ----- Resources (SEC-22, SEC-23, SEC-26) -----

    /// <summary>Multipart upload; the framework-level body limit is configured from Storage:MaxUploadSizeBytes (SEC-22).</summary>
    [HttpPost("content/{id:guid}/items/{itemId:guid}/resources")]
    [HasPermission(Permissions.Lms.ContentWrite)]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<ResourceResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ResourceResponse>> UploadResource(Guid id, Guid itemId, IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null)
        {
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]> { ["File"] = ["A file is required."] })
            {
                Type = "urn:opencampus:error:validation",
                Title = "validation",
                Status = StatusCodes.Status400BadRequest,
                Instance = Request.Path,
            });
        }

        await using var stream = file.OpenReadStream();
        var upload = new FileUpload(file.FileName, file.ContentType, file.Length, stream);
        var result = await content.UploadResourceAsync(id, itemId, upload, cancellationToken);
        if (result.IsFailure)
        {
            return StatusCode(ProblemMapping.StatusCodeFor(result.Error.Type), result.Error.ToProblemDetails(HttpContext));
        }

        // API-05: Location points at the authorised download of the new resource (SEC-25), on its own controller.
        return CreatedAtAction(nameof(ResourcesController.Download), "Resources", new { id = result.Value.Id }, result.Value);
    }

    [HttpDelete("content/{id:guid}/items/{itemId:guid}/resources/{resourceId:guid}")]
    [HasPermission(Permissions.Lms.ContentWrite)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveResource(Guid id, Guid itemId, Guid resourceId, CancellationToken cancellationToken) =>
        (await content.RemoveResourceAsync(id, itemId, resourceId, cancellationToken)).ToActionResult(this);
}

/// <summary>Authorised resource download (15.3, SEC-25): entitlement is verified before any byte is streamed; the store is never served statically (SEC-24).</summary>
[ApiController]
[Route("api/v1/resources")]
public sealed class ResourcesController(ResourceService resources) : ControllerBase
{
    [HttpGet("{id:guid}/download")]
    [HasPermission(Permissions.Lms.ContentRead)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Download(Guid id, CancellationToken cancellationToken)
    {
        var result = await resources.OpenForDownloadAsync(id, cancellationToken);
        if (result.IsFailure)
        {
            return StatusCode(ProblemMapping.StatusCodeFor(result.Error.Type), result.Error.ToProblemDetails(HttpContext));
        }

        var download = result.Value;
        // SEC-26: the recorded digest travels with the content so a client can verify integrity.
        Response.Headers["X-Content-SHA256"] = download.ContentHash;
        return File(download.Content, download.ContentType, download.FileName, enableRangeProcessing: false);
    }
}

/// <summary>The caller's own sections (17.1 instructor and learner shells): assigned as instructor, or enrolled as learner (SEC-12 scope).</summary>
[ApiController]
[Route("api/v1/me/sections")]
[Produces("application/json")]
public sealed class MySectionsController(ContentService content, LearnerContentService learners) : ControllerBase
{
    [HttpGet("teaching")]
    [HasPermission(Permissions.Lms.ContentWrite)]
    [ProducesResponseType<IReadOnlyList<SectionSummary>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SectionSummary>>> Teaching(CancellationToken cancellationToken) =>
        Ok(await content.ListMySectionsAsync(cancellationToken));

    [HttpGet("enrolled")]
    [HasPermission(Permissions.Lms.ContentRead)]
    [ProducesResponseType<IReadOnlyList<SectionSummary>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SectionSummary>>> Enrolled(CancellationToken cancellationToken) =>
        Ok(await learners.ListMySectionsAsync(cancellationToken));
}
