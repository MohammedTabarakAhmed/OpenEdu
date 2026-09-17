using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using OpenCampus.Api.ErrorHandling;
using OpenCampus.Api.Security;
using OpenCampus.Identity.Application.Authorization;
using OpenCampus.Sis.Application.Certificates;

namespace OpenCampus.Api.Controllers;

/// <summary>
/// Certificates by identifier (15.3 "Certification"): retrieval and download for the certificate's own learner or a
/// holder of learner administration — anyone else is answered "not found" (SEC-12, API-06) — and the one anonymous
/// endpoint of this increment, verification by code (15.4). Issuance lives on the enrolment route
/// (<see cref="EnrolmentCertificateController"/>) because the enrolment is the owning aggregate.
/// </summary>
[ApiController]
[Route("api/v1/certificates")]
[Produces("application/json")]
public sealed class CertificatesController(CertificateService certificates) : ControllerBase
{
    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.Sis.EnrolmentRead)]
    [ProducesResponseType<CertificateResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CertificateResponse>> Get(Guid id, CancellationToken cancellationToken) =>
        (await certificates.GetAsync(id, cancellationToken)).ToActionResult(this);

    /// <summary>The PDF document, served only through this authorised action (SEC-24/25).</summary>
    [HttpGet("{id:guid}/file")]
    [HasPermission(Permissions.Sis.EnrolmentRead)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Download(Guid id, CancellationToken cancellationToken)
    {
        var result = await certificates.OpenAsync(id, cancellationToken);
        if (result.IsFailure)
        {
            return StatusCode(ProblemMapping.StatusCodeFor(result.Error.Type), result.Error.ToProblemDetails(HttpContext));
        }

        var download = result.Value;
        return File(download.Content, download.ContentType, download.FileName, enableRangeProcessing: false);
    }

    /// <summary>
    /// Anonymous verification by code (15.4, justified in the README). Rate-limited like the other anonymous
    /// endpoints (SEC-16); the code space (≈99 bits) makes enumeration impractical regardless. The answer names what
    /// the certificate certifies and nothing internal.
    /// </summary>
    [HttpGet("verify/{code}")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Authentication)]
    [ProducesResponseType<CertificateVerificationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CertificateVerificationResponse>> Verify(string code, CancellationToken cancellationToken) =>
        (await certificates.VerifyAsync(code, cancellationToken)).ToActionResult(this);
}

/// <summary>Issuance and administrative lookup on the owning enrolment (BR-10/BR-11 decided by the aggregate).</summary>
[ApiController]
[Route("api/v1/enrolments/{enrolmentId:guid}/certificate")]
[Produces("application/json")]
public sealed class EnrolmentCertificateController(CertificateService certificates) : ControllerBase
{
    /// <summary>Issues the certificate: 201 with Location (API-05); 422 with the rule code under BR-10/BR-11 (API-07).</summary>
    [HttpPost]
    [HasPermission(Permissions.Sis.CertificateIssue)]
    [ProducesResponseType<CertificateResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CertificateResponse>> Issue(Guid enrolmentId, CancellationToken cancellationToken) =>
        (await certificates.IssueAsync(enrolmentId, cancellationToken))
            .ToCreatedResult(this, nameof(CertificatesController.Get), r => new { controller = "Certificates", id = r.Id });

    [HttpGet]
    [HasPermission(Permissions.Sis.LearnerRead)]
    [ProducesResponseType<CertificateResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CertificateResponse>> Get(Guid enrolmentId, CancellationToken cancellationToken) =>
        (await certificates.GetForEnrolmentAsync(enrolmentId, cancellationToken)).ToActionResult(this);
}

/// <summary>Administrative listing of one learner's certificates.</summary>
[ApiController]
[Route("api/v1/learners/{learnerId:guid}/certificates")]
[Produces("application/json")]
public sealed class LearnerCertificatesController(CertificateService certificates) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Sis.LearnerRead)]
    [ProducesResponseType<IReadOnlyList<CertificateResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<CertificateResponse>>> List(Guid learnerId, CancellationToken cancellationToken) =>
        (await certificates.ListForLearnerAsync(learnerId, cancellationToken)).ToActionResult(this);
}

/// <summary>Certificate listing for the calling learner (15.3 "certificate listing/download").</summary>
[ApiController]
[Route("api/v1/me/certificates")]
[Produces("application/json")]
public sealed class MyCertificatesController(CertificateService certificates) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Sis.EnrolmentRead)]
    [ProducesResponseType<IReadOnlyList<CertificateResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<CertificateResponse>>> List(CancellationToken cancellationToken) =>
        (await certificates.ListMineAsync(cancellationToken)).ToActionResult(this);
}
