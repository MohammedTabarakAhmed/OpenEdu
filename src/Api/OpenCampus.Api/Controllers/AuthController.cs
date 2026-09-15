using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using OpenCampus.Api.ErrorHandling;
using OpenCampus.Api.Security;
using OpenCampus.Identity.Application.Abstractions;
using OpenCampus.Identity.Application.Authentication;
using OpenCampus.Identity.Application.Security;

namespace OpenCampus.Api.Controllers;

/// <summary>
/// Authentication and session capability (SDD 15.3). Anonymous endpoints are limited to
/// credential authentication, multi-factor verification and session refresh (SDD 15.4).
/// Controllers bind, delegate, and translate results only (LR-04).
/// </summary>
[ApiController]
[Route("api/v1/auth")]
[Produces("application/json")]
public sealed class AuthController(AuthenticationService authentication, ICurrentUser currentUser) : ControllerBase
{
    /// <summary>Credential authentication. Yields a session, or an MFA challenge where MFA is enabled (SEC-09).</summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Authentication)]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var result = await authentication.LoginAsync(request, ClientContext(), cancellationToken);
        if (result.IsFailure)
        {
            return StatusCode(ProblemMapping.StatusCodeFor(result.Error.Type), result.Error.ToProblemDetails(HttpContext));
        }

        if (result.Value.RequiresMfa)
        {
            return Ok(new LoginResponse(true, result.Value.MfaChallenge!.Challenge, null));
        }

        var authenticated = result.Value.Authenticated!;
        RefreshCookie.Set(Response, authenticated.RefreshToken, authenticated.RefreshTokenExpiresAtUtc);
        return Ok(new LoginResponse(false, null, authenticated.Response));
    }

    /// <summary>Multi-factor verification completing an MFA-gated login (SEC-09).</summary>
    [HttpPost("mfa/verify")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Authentication)]
    [ProducesResponseType<AuthenticationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthenticationResponse>> VerifyMfa(MfaVerifyRequest request, CancellationToken cancellationToken)
    {
        var result = await authentication.VerifyMfaAsync(request, ClientContext(), cancellationToken);
        if (result.IsFailure)
        {
            return StatusCode(ProblemMapping.StatusCodeFor(result.Error.Type), result.Error.ToProblemDetails(HttpContext));
        }

        RefreshCookie.Set(Response, result.Value.RefreshToken, result.Value.RefreshTokenExpiresAtUtc);
        return Ok(result.Value.Response);
    }

    /// <summary>Session refresh with rotation (SEC-07). The credential travels only in the cookie (SEC-05).</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Authentication)]
    [ProducesResponseType<AuthenticationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthenticationResponse>> Refresh(CancellationToken cancellationToken)
    {
        var presented = RefreshCookie.Read(Request);
        if (presented is null)
        {
            return Unauthorized(AuthenticationErrors.InvalidSession.ToProblemDetails(HttpContext));
        }

        var result = await authentication.RefreshAsync(presented, ClientContext(), cancellationToken);
        if (result.IsFailure)
        {
            RefreshCookie.Clear(Response);
            return StatusCode(ProblemMapping.StatusCodeFor(result.Error.Type), result.Error.ToProblemDetails(HttpContext));
        }

        RefreshCookie.Set(Response, result.Value.RefreshToken, result.Value.RefreshTokenExpiresAtUtc);
        return Ok(result.Value.Response);
    }

    /// <summary>Session termination (SEC-08).</summary>
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        await authentication.LogoutAsync(RefreshCookie.Read(Request), ClientContext(), cancellationToken);
        RefreshCookie.Clear(Response);
        return NoContent();
    }

    /// <summary>The authenticated principal with roles and permissions.</summary>
    [HttpGet("me")]
    [ProducesResponseType<PrincipalResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PrincipalResponse>> Me(CancellationToken cancellationToken)
    {
        var result = await authentication.GetPrincipalAsync(RequireUserId(), cancellationToken);
        return result.ToActionResult(this);
    }

    [HttpPut("password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        var result = await authentication.ChangePasswordAsync(RequireUserId(), request, cancellationToken);
        return result.ToActionResult(this);
    }

    /// <summary>Multi-factor enrolment: the only response that ever carries the shared secret, and only once.</summary>
    [HttpPost("mfa/enrolment")]
    [ProducesResponseType<MfaEnrolmentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<MfaEnrolmentResponse>> BeginMfaEnrolment(CancellationToken cancellationToken)
    {
        var result = await authentication.BeginMfaEnrolmentAsync(RequireUserId(), cancellationToken);
        return result.ToActionResult(this);
    }

    [HttpPost("mfa/enrolment/confirm")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ConfirmMfaEnrolment(MfaConfirmRequest request, CancellationToken cancellationToken)
    {
        var result = await authentication.ConfirmMfaEnrolmentAsync(RequireUserId(), request, cancellationToken);
        return result.ToActionResult(this);
    }

    private Guid RequireUserId() =>
        currentUser.UserId ?? throw new InvalidOperationException("An authenticated endpoint was reached without a principal.");

    private ClientContext ClientContext() =>
        new(HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString());
}

/// <summary>Body of the credential step: either a session or an MFA challenge, never both.</summary>
public sealed record LoginResponse(bool MfaRequired, string? Challenge, AuthenticationResponse? Authenticated);
