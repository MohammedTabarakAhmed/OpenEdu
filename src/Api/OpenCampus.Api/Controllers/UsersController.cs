using Microsoft.AspNetCore.Mvc;
using OpenCampus.Api.ErrorHandling;
using OpenCampus.Api.Security;
using OpenCampus.Identity.Application.Administration;
using OpenCampus.Identity.Application.Authorization;
using OpenCampus.Identity.Application.Security;
using OpenCampus.SharedKernel;

namespace OpenCampus.Api.Controllers;

/// <summary>User administration capability (SDD 15.3). Every action declares its permission policy (SEC-11).</summary>
[ApiController]
[Route("api/v1/users")]
[Produces("application/json")]
public sealed class UsersController(UserAdministrationService administration) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Identity.UserRead)]
    [ProducesResponseType<PagedResponse<UserResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<UserResponse>>> List([FromQuery] UserListQuery query, CancellationToken cancellationToken) =>
        Ok(await administration.ListAsync(query, cancellationToken));

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.Identity.UserRead)]
    [ProducesResponseType<UserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserResponse>> Get(Guid id, CancellationToken cancellationToken) =>
        (await administration.GetAsync(id, cancellationToken)).ToActionResult(this);

    [HttpPost]
    [HasPermission(Permissions.Identity.UserWrite)]
    [ProducesResponseType<UserResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserResponse>> Create(CreateUserRequest request, CancellationToken cancellationToken)
    {
        var result = await administration.CreateAsync(request, ClientContext(), cancellationToken);
        if (result.IsFailure)
        {
            return StatusCode(ProblemMapping.StatusCodeFor(result.Error.Type), result.Error.ToProblemDetails(HttpContext));
        }

        // API-05: creation returns 201 with a location header.
        return CreatedAtAction(nameof(Get), new { id = result.Value.Id }, result.Value);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.Identity.UserWrite)]
    [ProducesResponseType<UserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserResponse>> Update(Guid id, UpdateUserRequest request, CancellationToken cancellationToken) =>
        (await administration.UpdateAsync(id, request, cancellationToken)).ToActionResult(this);

    /// <summary>Logical deactivation (DC-03); the record is retained. Returns 204 (API-05).</summary>
    [HttpDelete("{id:guid}")]
    [HasPermission(Permissions.Identity.UserDeactivate)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken) =>
        (await administration.DeactivateAsync(id, ClientContext(), cancellationToken)).ToActionResult(this);

    [HttpPost("{id:guid}/activate")]
    [HasPermission(Permissions.Identity.UserDeactivate)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Activate(Guid id, CancellationToken cancellationToken) =>
        (await administration.ActivateAsync(id, cancellationToken)).ToActionResult(this);

    [HttpPut("{id:guid}/roles")]
    [HasPermission(Permissions.Identity.RoleAssign)]
    [ProducesResponseType<UserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserResponse>> AssignRoles(Guid id, AssignRolesRequest request, CancellationToken cancellationToken) =>
        (await administration.AssignRolesAsync(id, request, ClientContext(), cancellationToken)).ToActionResult(this);

    [HttpGet("{id:guid}/sessions")]
    [HasPermission(Permissions.Identity.SessionRevoke)]
    [ProducesResponseType<IReadOnlyList<SessionResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SessionResponse>>> ListSessions(Guid id, CancellationToken cancellationToken) =>
        (await administration.ListSessionsAsync(id, cancellationToken)).ToActionResult(this);

    [HttpDelete("{id:guid}/sessions/{sessionId:guid}")]
    [HasPermission(Permissions.Identity.SessionRevoke)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RevokeSession(Guid id, Guid sessionId, CancellationToken cancellationToken) =>
        (await administration.RevokeSessionAsync(id, sessionId, ClientContext(), cancellationToken)).ToActionResult(this);

    [HttpDelete("{id:guid}/sessions")]
    [HasPermission(Permissions.Identity.SessionRevoke)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RevokeAllSessions(Guid id, CancellationToken cancellationToken) =>
        (await administration.RevokeAllSessionsAsync(id, ClientContext(), cancellationToken)).ToActionResult(this);

    private ClientContext ClientContext() =>
        new(HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString());
}

[ApiController]
[Route("api/v1/roles")]
[Produces("application/json")]
public sealed class RolesController(UserAdministrationService administration) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Identity.UserRead)]
    [ProducesResponseType<IReadOnlyList<RoleResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<RoleResponse>>> List(CancellationToken cancellationToken) =>
        Ok(await administration.ListRolesAsync(cancellationToken));
}

/// <summary>Read-only: audit records are not modifiable or deletable through any interface (SEC-32).</summary>
[ApiController]
[Route("api/v1/audit")]
[Produces("application/json")]
public sealed class AuditController(UserAdministrationService administration) : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Identity.AuditRead)]
    [ProducesResponseType<PagedResponse<AuditEventResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<AuditEventResponse>>> List([FromQuery] AuditListQuery query, CancellationToken cancellationToken) =>
        Ok(await administration.ListAuditAsync(query, cancellationToken));
}
