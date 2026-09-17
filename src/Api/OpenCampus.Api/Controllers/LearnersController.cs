using Microsoft.AspNetCore.Mvc;
using OpenCampus.Api.ErrorHandling;
using OpenCampus.Api.Security;
using OpenCampus.Identity.Application.Authorization;
using OpenCampus.SharedKernel;
using OpenCampus.Sis.Application.Catalogue;
using OpenCampus.Sis.Application.Enrolments;
using OpenCampus.Sis.Application.Grading;
using OpenCampus.Sis.Application.Learners;

namespace OpenCampus.Api.Controllers;

/// <summary>Learner capability (15.3 "Learner and enrolment"): listing, creation, retrieval, amendment, transcript.</summary>
[ApiController]
[Route("api/v1/learners")]
[Produces("application/json")]
public sealed class LearnersController(LearnerService learners, EnrolmentService enrolments) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Sis.LearnerRead)]
    [ProducesResponseType<PagedResponse<LearnerResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<LearnerResponse>>> List([FromQuery] LearnerListQuery query, CancellationToken cancellationToken) =>
        Ok(await learners.ListAsync(query, cancellationToken));

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.Sis.LearnerRead)]
    [ProducesResponseType<LearnerResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<LearnerResponse>> Get(Guid id, CancellationToken cancellationToken) =>
        (await learners.GetAsync(id, cancellationToken)).ToActionResult(this);

    [HttpPost]
    [HasPermission(Permissions.Sis.LearnerWrite)]
    [ProducesResponseType<LearnerResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<LearnerResponse>> Create(CreateLearnerRequest request, CancellationToken cancellationToken) =>
        (await learners.CreateAsync(request, cancellationToken)).ToCreatedResult(this, nameof(Get), r => new { id = r.Id });

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.Sis.LearnerWrite)]
    [ProducesResponseType<LearnerResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<LearnerResponse>> Update(Guid id, UpdateLearnerRequest request, CancellationToken cancellationToken) =>
        (await learners.UpdateAsync(id, request, cancellationToken)).ToActionResult(this);

    /// <summary>Enrolment listing by learner (15.3). Administrative scope: requires learner-record access as well (SEC-12).</summary>
    [HttpGet("{id:guid}/enrolments")]
    [HasPermission(Permissions.Sis.EnrolmentRead)]
    [HasPermission(Permissions.Sis.LearnerRead)]
    [ProducesResponseType<PagedResponse<EnrolmentResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<EnrolmentResponse>>> ListEnrolments(Guid id, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken) =>
        Ok(await enrolments.ListAsync(new EnrolmentListQuery(page, pageSize, id, null, null), cancellationToken));

    [HttpGet("{id:guid}/transcript")]
    [HasPermission(Permissions.Sis.ReportRead)]
    [ProducesResponseType<TranscriptResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TranscriptResponse>> Transcript(Guid id, CancellationToken cancellationToken) =>
        (await enrolments.GetTranscriptAsync(id, cancellationToken)).ToActionResult(this);

    /// <summary>Learner-role accounts without a learner record, for the administrative interface.</summary>
    [HttpGet("unlinked-users")]
    [HasPermission(Permissions.Sis.LearnerWrite)]
    [ProducesResponseType<IReadOnlyList<LearnerUserSummary>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<LearnerUserSummary>>> UnlinkedUsers(CancellationToken cancellationToken) =>
        Ok(await learners.ListUnlinkedLearnerUsersAsync(cancellationToken));
}

/// <summary>
/// Administrative enrolment capability (15.3): creation, retrieval, withdrawal, listing by learner or section.
/// A permission code grants capability, not scope (Appendix C): the Learner role holds sis.enrolment.read for
/// its own records, so every administrative route here additionally requires sis.learner.read, which learners
/// do not hold. Learner-scoped access is api/v1/me/enrolments (SEC-12).
/// </summary>
[ApiController]
[Route("api/v1/enrolments")]
[Produces("application/json")]
[HasPermission(Permissions.Sis.LearnerRead)]
public sealed class EnrolmentsController(EnrolmentService enrolments) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Sis.EnrolmentRead)]
    [ProducesResponseType<PagedResponse<EnrolmentResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<EnrolmentResponse>>> List([FromQuery] EnrolmentListQuery query, CancellationToken cancellationToken) =>
        Ok(await enrolments.ListAsync(query, cancellationToken));

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.Sis.EnrolmentRead)]
    [ProducesResponseType<EnrolmentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EnrolmentResponse>> Get(Guid id, CancellationToken cancellationToken) =>
        (await enrolments.GetAsync(id, cancellationToken)).ToActionResult(this);

    /// <summary>422 with the rule reference on BR-01 (capacity), BR-02 (duplicate) or BR-03 (not open).</summary>
    [HttpPost]
    [HasPermission(Permissions.Sis.EnrolmentWrite)]
    [ProducesResponseType<EnrolmentResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<EnrolmentResponse>> Create(CreateEnrolmentRequest request, CancellationToken cancellationToken) =>
        (await enrolments.CreateAsync(request, cancellationToken)).ToCreatedResult(this, nameof(Get), r => new { id = r.Id });

    /// <summary>Withdrawal; the enrolment row is retained with status Withdrawn (DC-03).</summary>
    [HttpDelete("{id:guid}")]
    [HasPermission(Permissions.Sis.EnrolmentWrite)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Withdraw(Guid id, CancellationToken cancellationToken) =>
        (await enrolments.WithdrawAsync(id, cancellationToken)).ToActionResult(this);
}

/// <summary>
/// Learner self-service (15.3): catalogue browsing, self-enrolment, enrolled course listing, withdrawal and
/// transcript, all scoped to the calling user's learner record (SEC-12).
/// </summary>
[ApiController]
[Route("api/v1/catalogue")]
[Produces("application/json")]
public sealed class CatalogueController(LearnerSelfService selfService) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Sis.SectionRead)]
    [ProducesResponseType<PagedResponse<CatalogueEntry>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<CatalogueEntry>>> Browse([FromQuery] CatalogueQuery query, CancellationToken cancellationToken) =>
        Ok(await selfService.BrowseAsync(query, cancellationToken));

    [HttpGet("{sectionId:guid}")]
    [HasPermission(Permissions.Sis.SectionRead)]
    [ProducesResponseType<CatalogueEntryDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CatalogueEntryDetail>> Get(Guid sectionId, CancellationToken cancellationToken) =>
        (await selfService.GetAsync(sectionId, cancellationToken)).ToActionResult(this);
}

[ApiController]
[Route("api/v1/me/enrolments")]
[Produces("application/json")]
public sealed class MyEnrolmentsController(LearnerSelfService selfService) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Sis.EnrolmentRead)]
    [ProducesResponseType<PagedResponse<EnrolmentResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagedResponse<EnrolmentResponse>>> List([FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] bool activeOnly, CancellationToken cancellationToken) =>
        (await selfService.ListMyEnrolmentsAsync(page, pageSize, activeOnly, cancellationToken)).ToActionResult(this);

    [HttpPost]
    [HasPermission(Permissions.Sis.EnrolmentWrite)]
    [ProducesResponseType<EnrolmentResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<EnrolmentResponse>> Enrol(SelfEnrolRequest request, CancellationToken cancellationToken) =>
        (await selfService.EnrolAsync(request, cancellationToken)).ToCreatedResult(this, nameof(List), _ => null);

    [HttpDelete("{id:guid}")]
    [HasPermission(Permissions.Sis.EnrolmentWrite)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Withdraw(Guid id, CancellationToken cancellationToken) =>
        (await selfService.WithdrawAsync(id, cancellationToken)).ToActionResult(this);

    [HttpGet("transcript")]
    [HasPermission(Permissions.Sis.EnrolmentRead)]
    [ProducesResponseType<TranscriptResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TranscriptResponse>> Transcript(CancellationToken cancellationToken) =>
        (await selfService.MyTranscriptAsync(cancellationToken)).ToActionResult(this);

    /// <summary>Released grade retrieval (15.3; BR-06): the caller's own enrolment only.</summary>
    [HttpGet("{id:guid}/results")]
    [HasPermission(Permissions.Sis.GradeRead)]
    [ProducesResponseType<LearnerResultsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<LearnerResultsResponse>> Results(Guid id, CancellationToken cancellationToken) =>
        (await selfService.MyResultsAsync(id, cancellationToken)).ToActionResult(this);
}
