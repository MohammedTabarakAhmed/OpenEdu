using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using OpenCampus.Api.ErrorHandling;
using OpenCampus.Api.Security;
using OpenCampus.Identity.Application.Registration;
using OpenCampus.Identity.Application.Security;

namespace OpenCampus.Api.Controllers;

/// <summary>
/// Self-registration (Increment 7, beyond the SDD). The three actions are anonymous by necessity and therefore sit
/// under the authentication rate limit (SEC-16). Register and resend always answer 202 with the same body so the
/// e-mail directory cannot be enumerated; verify answers 404 for any unusable token. Controllers bind, delegate and
/// translate results only (LR-04).
/// </summary>
[ApiController]
[Route("api/v1/registration")]
[Produces("application/json")]
[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicies.Authentication)]
public sealed class RegistrationController(RegistrationService registration) : ControllerBase
{
    /// <summary>Creates a pending account and sends the verification message. 409 only for a taken user name.</summary>
    [HttpPost]
    [ProducesResponseType<RegistrationAcceptedResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RegistrationAcceptedResponse>> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        var result = await registration.RegisterAsync(request, ClientContext(), cancellationToken);
        return result.IsFailure
            ? StatusCode(ProblemMapping.StatusCodeFor(result.Error.Type), result.Error.ToProblemDetails(HttpContext))
            : Accepted(result.Value);
    }

    /// <summary>Consumes a verification token (POST, never GET — mail scanners pre-fetch links).</summary>
    [HttpPost("verify-email")]
    [ProducesResponseType<VerifyEmailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<VerifyEmailResponse>> VerifyEmail(VerifyEmailRequest request, CancellationToken cancellationToken) =>
        (await registration.VerifyEmailAsync(request, ClientContext(), cancellationToken)).ToActionResult(this);

    /// <summary>Re-sends the verification message when one is due; the answer never reveals whether the address is known.</summary>
    [HttpPost("resend-verification")]
    [ProducesResponseType<RegistrationAcceptedResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RegistrationAcceptedResponse>> ResendVerification(ResendVerificationRequest request, CancellationToken cancellationToken)
    {
        var result = await registration.ResendVerificationAsync(request, ClientContext(), cancellationToken);
        return result.IsFailure
            ? StatusCode(ProblemMapping.StatusCodeFor(result.Error.Type), result.Error.ToProblemDetails(HttpContext))
            : Accepted(RegistrationAcceptedResponse.Default);
    }

    private ClientContext ClientContext() =>
        new(HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString());
}
