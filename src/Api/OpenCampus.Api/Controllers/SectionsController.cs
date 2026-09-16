using Microsoft.AspNetCore.Mvc;
using OpenCampus.Api.ErrorHandling;
using OpenCampus.Api.Security;
using OpenCampus.Identity.Application.Authorization;
using OpenCampus.SharedKernel;
using OpenCampus.Sis.Application.Enrolments;
using OpenCampus.Sis.Application.Sections;

namespace OpenCampus.Api.Controllers;

/// <summary>
/// Section capability (15.3 "Academic structure"): listing, creation, retrieval, amendment, deletion,
/// state transition, scheduled session management and grade scheme definition. Section-14 violations
/// surface as 422 with the rule reference (BR-14, BR-16).
/// </summary>
[ApiController]
[Route("api/v1/sections")]
[Produces("application/json")]
public sealed class SectionsController(SectionService sections, EnrolmentService enrolments) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Sis.SectionRead)]
    [ProducesResponseType<PagedResponse<SectionResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<SectionResponse>>> List([FromQuery] SectionListQuery query, CancellationToken cancellationToken) =>
        Ok(await sections.ListAsync(query, cancellationToken));

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.Sis.SectionRead)]
    [ProducesResponseType<SectionDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SectionDetailResponse>> Get(Guid id, CancellationToken cancellationToken) =>
        (await sections.GetAsync(id, cancellationToken)).ToActionResult(this);

    [HttpPost]
    [HasPermission(Permissions.Sis.SectionWrite)]
    [ProducesResponseType<SectionDetailResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SectionDetailResponse>> Create(CreateSectionRequest request, CancellationToken cancellationToken) =>
        (await sections.CreateAsync(request, cancellationToken)).ToCreatedResult(this, nameof(Get), r => new { id = r.Section.Id });

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.Sis.SectionWrite)]
    [ProducesResponseType<SectionDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SectionDetailResponse>> Update(Guid id, UpdateSectionRequest request, CancellationToken cancellationToken) =>
        (await sections.UpdateAsync(id, request, cancellationToken)).ToActionResult(this);

    /// <summary>Logical deletion (DC-03); 422 BR-14 while any enrolment exists.</summary>
    [HttpDelete("{id:guid}")]
    [HasPermission(Permissions.Sis.SectionWrite)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        (await sections.DeleteAsync(id, cancellationToken)).ToActionResult(this);

    // ----- State transitions -----

    [HttpPost("{id:guid}/open")]
    [HasPermission(Permissions.Sis.SectionOpen)]
    [ProducesResponseType<SectionDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<SectionDetailResponse>> Open(Guid id, CancellationToken cancellationToken) =>
        (await sections.OpenAsync(id, cancellationToken)).ToActionResult(this);

    [HttpPost("{id:guid}/close")]
    [HasPermission(Permissions.Sis.SectionOpen)]
    [ProducesResponseType<SectionDetailResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<SectionDetailResponse>> Close(Guid id, CancellationToken cancellationToken) =>
        (await sections.CloseAsync(id, cancellationToken)).ToActionResult(this);

    [HttpPost("{id:guid}/cancel")]
    [HasPermission(Permissions.Sis.SectionOpen)]
    [ProducesResponseType<SectionDetailResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<SectionDetailResponse>> Cancel(Guid id, CancellationToken cancellationToken) =>
        (await sections.CancelAsync(id, cancellationToken)).ToActionResult(this);

    // ----- Scheduled sessions -----

    [HttpPost("{id:guid}/sessions")]
    [HasPermission(Permissions.Sis.SectionWrite)]
    [ProducesResponseType<SessionResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<SessionResponse>> AddSession(Guid id, SessionRequest request, CancellationToken cancellationToken) =>
        (await sections.AddSessionAsync(id, request, cancellationToken)).ToCreatedResult(this, nameof(Get), _ => new { id });

    [HttpPut("{id:guid}/sessions/{sessionId:guid}")]
    [HasPermission(Permissions.Sis.SectionWrite)]
    [ProducesResponseType<SessionResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<SessionResponse>> RescheduleSession(Guid id, Guid sessionId, SessionRequest request, CancellationToken cancellationToken) =>
        (await sections.RescheduleSessionAsync(id, sessionId, request, cancellationToken)).ToActionResult(this);

    [HttpDelete("{id:guid}/sessions/{sessionId:guid}")]
    [HasPermission(Permissions.Sis.SectionWrite)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveSession(Guid id, Guid sessionId, CancellationToken cancellationToken) =>
        (await sections.RemoveSessionAsync(id, sessionId, cancellationToken)).ToActionResult(this);

    // ----- Grade scheme -----

    [HttpPost("{id:guid}/grade-components")]
    [HasPermission(Permissions.Sis.SectionWrite)]
    [ProducesResponseType<GradeComponentResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<GradeComponentResponse>> AddGradeComponent(Guid id, GradeComponentRequest request, CancellationToken cancellationToken) =>
        (await sections.AddGradeComponentAsync(id, request, cancellationToken)).ToCreatedResult(this, nameof(Get), _ => new { id });

    [HttpPut("{id:guid}/grade-components/{componentId:guid}")]
    [HasPermission(Permissions.Sis.SectionWrite)]
    [ProducesResponseType<GradeComponentResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<GradeComponentResponse>> AmendGradeComponent(Guid id, Guid componentId, GradeComponentRequest request, CancellationToken cancellationToken) =>
        (await sections.AmendGradeComponentAsync(id, componentId, request, cancellationToken)).ToActionResult(this);

    [HttpDelete("{id:guid}/grade-components/{componentId:guid}")]
    [HasPermission(Permissions.Sis.SectionWrite)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveGradeComponent(Guid id, Guid componentId, CancellationToken cancellationToken) =>
        (await sections.RemoveGradeComponentAsync(id, componentId, cancellationToken)).ToActionResult(this);

    // ----- Enrolments of the section (15.3 "enrolment listing by section") -----

    /// <summary>Administrative scope (SEC-12): learner-record access is required as well. Instructor access to assigned sections arrives with the SEC-12 handlers of Increment 4.</summary>
    [HttpGet("{id:guid}/enrolments")]
    [HasPermission(Permissions.Sis.EnrolmentRead)]
    [HasPermission(Permissions.Sis.LearnerRead)]
    [ProducesResponseType<PagedResponse<EnrolmentResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<EnrolmentResponse>>> ListEnrolments(Guid id, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken) =>
        Ok(await enrolments.ListAsync(new EnrolmentListQuery(page, pageSize, null, id, null), cancellationToken));

    /// <summary>Instructors eligible for assignment (active Instructor-role accounts), for the administrative interface.</summary>
    [HttpGet("instructors")]
    [HasPermission(Permissions.Sis.SectionWrite)]
    [ProducesResponseType<IReadOnlyList<InstructorSummary>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<InstructorSummary>>> ListInstructors(CancellationToken cancellationToken) =>
        Ok(await sections.ListInstructorsAsync(cancellationToken));
}
