using Microsoft.AspNetCore.Mvc;
using OpenCampus.Api.ErrorHandling;
using OpenCampus.Api.Security;
using OpenCampus.Identity.Application.Authorization;
using OpenCampus.SharedKernel;
using OpenCampus.Sis.Application.Courses;
using OpenCampus.Sis.Application.Programmes;

namespace OpenCampus.Api.Controllers;

/// <summary>Programme capability (15.3 "Academic structure"). Controllers bind, delegate and translate only (LR-04).</summary>
[ApiController]
[Route("api/v1/programmes")]
[Produces("application/json")]
public sealed class ProgrammesController(ProgrammeService programmes) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Sis.ProgrammeRead)]
    [ProducesResponseType<PagedResponse<ProgrammeResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<ProgrammeResponse>>> List([FromQuery] ProgrammeListQuery query, CancellationToken cancellationToken) =>
        Ok(await programmes.ListAsync(query, cancellationToken));

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.Sis.ProgrammeRead)]
    [ProducesResponseType<ProgrammeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProgrammeResponse>> Get(Guid id, CancellationToken cancellationToken) =>
        (await programmes.GetAsync(id, cancellationToken)).ToActionResult(this);

    [HttpPost]
    [HasPermission(Permissions.Sis.ProgrammeWrite)]
    [ProducesResponseType<ProgrammeResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProgrammeResponse>> Create(CreateProgrammeRequest request, CancellationToken cancellationToken) =>
        (await programmes.CreateAsync(request, cancellationToken)).ToCreatedResult(this, nameof(Get), r => new { id = r.Id });

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.Sis.ProgrammeWrite)]
    [ProducesResponseType<ProgrammeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProgrammeResponse>> Update(Guid id, UpdateProgrammeRequest request, CancellationToken cancellationToken) =>
        (await programmes.UpdateAsync(id, request, cancellationToken)).ToActionResult(this);

    /// <summary>Logical deletion (DC-03). 409 while courses belong to the programme.</summary>
    [HttpDelete("{id:guid}")]
    [HasPermission(Permissions.Sis.ProgrammeWrite)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        (await programmes.DeleteAsync(id, cancellationToken)).ToActionResult(this);
}

[ApiController]
[Route("api/v1/courses")]
[Produces("application/json")]
public sealed class CoursesController(CourseService courses) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Sis.CourseRead)]
    [ProducesResponseType<PagedResponse<CourseResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<CourseResponse>>> List([FromQuery] CourseListQuery query, CancellationToken cancellationToken) =>
        Ok(await courses.ListAsync(query, cancellationToken));

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.Sis.CourseRead)]
    [ProducesResponseType<CourseResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CourseResponse>> Get(Guid id, CancellationToken cancellationToken) =>
        (await courses.GetAsync(id, cancellationToken)).ToActionResult(this);

    [HttpPost]
    [HasPermission(Permissions.Sis.CourseWrite)]
    [ProducesResponseType<CourseResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CourseResponse>> Create(CreateCourseRequest request, CancellationToken cancellationToken) =>
        (await courses.CreateAsync(request, cancellationToken)).ToCreatedResult(this, nameof(Get), r => new { id = r.Id });

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.Sis.CourseWrite)]
    [ProducesResponseType<CourseResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CourseResponse>> Update(Guid id, UpdateCourseRequest request, CancellationToken cancellationToken) =>
        (await courses.UpdateAsync(id, request, cancellationToken)).ToActionResult(this);

    /// <summary>Logical deletion (DC-03). 409 while sections belong to the course.</summary>
    [HttpDelete("{id:guid}")]
    [HasPermission(Permissions.Sis.CourseWrite)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        (await courses.DeleteAsync(id, cancellationToken)).ToActionResult(this);
}
