using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using OpenCampus.Identity.Application.Administration;
using OpenCampus.Identity.Application.Authorization;
using OpenCampus.Identity.Domain.Audit;
using OpenCampus.SharedKernel;
using static OpenCampus.IntegrationTests.Identity.AuthTestSupport;

namespace OpenCampus.IntegrationTests.Identity;

/// <summary>User administration capability (SDD 15.3) end to end, including the audit events of SEC-30.</summary>
[Collection(ApiCollection.Name)]
public class UserAdministrationTests(ApiFactory factory)
{
    private static CreateUserRequest NewUserRequest(params string[] roles)
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        return new CreateUserRequest($"new_{suffix}", $"new_{suffix}@example.org", "Initial-Password-123", $"New {suffix}", "جديد", roles);
    }

    private async Task<(HttpClient Client, string Token, Guid AdminId)> AdminClientAsync()
    {
        var client = factory.CreateApiClient();
        var (admin, token, _) = await LoginAsRoleAsync(factory, client, RoleNames.Administrator);
        return (client, token, admin.Id);
    }

    private static HttpRequestMessage Request(HttpMethod method, string url, string token, object? body = null) =>
        new HttpRequestMessage(method, url) { Content = body is null ? null : JsonContent.Create(body) }.WithBearer(token);

    // API-05: creation returns 201 with a location header. SEC-30: role assignment is audited.
    [Fact]
    public async Task Create_ReturnsCreatedWithLocation_AndAuditsRoleAssignment()
    {
        var (client, token, adminId) = await AdminClientAsync();
        using (client)
        {
            var request = NewUserRequest(RoleNames.Learner);

            var response = await client.SendAsync(Request(HttpMethod.Post, "/api/v1/users", token, request));

            response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
            var created = (await response.Content.ReadFromJsonAsync<UserResponse>(Json))!;
            response.Headers.Location!.ToString().ShouldEndWith($"/api/v1/users/{created.Id}");
            created.UserName.ShouldBe(request.UserName);
            created.Roles.ShouldBe([RoleNames.Learner]);
            created.IsActive.ShouldBeTrue();

            var audits = await AuditEventsAsync(factory, adminId, AuditEventTypes.RoleAssigned);
            audits.ShouldContain(a => a.EntityId == created.Id && a.DetailsJson!.Contains(RoleNames.Learner));

            // The new account can authenticate.
            (await LoginAsync(client, request.UserName, request.Password)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task Create_WithDuplicateUserNameOrEmail_Returns409()
    {
        var (client, token, _) = await AdminClientAsync();
        using (client)
        {
            var request = NewUserRequest(RoleNames.Learner);
            (await client.SendAsync(Request(HttpMethod.Post, "/api/v1/users", token, request))).StatusCode.ShouldBe(HttpStatusCode.Created);

            var duplicateName = await client.SendAsync(Request(HttpMethod.Post, "/api/v1/users", token, request with { Email = "other@example.org" }));
            duplicateName.StatusCode.ShouldBe(HttpStatusCode.Conflict);

            var duplicateEmail = await client.SendAsync(Request(HttpMethod.Post, "/api/v1/users", token, request with { UserName = "other_name" }));
            duplicateEmail.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        }
    }

    [Fact]
    public async Task Create_WithInvalidFieldsOrUnknownRole_Returns400()
    {
        var (client, token, _) = await AdminClientAsync();
        using (client)
        {
            var invalid = NewUserRequest(RoleNames.Learner) with { Email = "not-an-email", Password = "short" };
            var response = await client.SendAsync(Request(HttpMethod.Post, "/api/v1/users", token, invalid));
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            var problem = (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(Json))!;
            problem.Errors.Keys.ShouldContain(nameof(CreateUserRequest.Email));
            problem.Errors.Keys.ShouldContain(nameof(CreateUserRequest.Password));

            var unknownRole = await client.SendAsync(Request(HttpMethod.Post, "/api/v1/users", token, NewUserRequest("Wizard")));
            unknownRole.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }
    }

    // API-03: pagination envelope with page, page size, total count and total pages.
    [Fact]
    public async Task List_ReturnsPagedEnvelopeWithSearchAndSort()
    {
        var (client, token, _) = await AdminClientAsync();
        using (client)
        {
            var marker = Guid.NewGuid().ToString("N")[..8];
            for (var i = 0; i < 3; i++)
            {
                var request = NewUserRequest(RoleNames.Learner) with { FullNameEn = $"Marker {marker} {i}" };
                (await client.SendAsync(Request(HttpMethod.Post, "/api/v1/users", token, request))).StatusCode.ShouldBe(HttpStatusCode.Created);
            }

            var response = await client.SendAsync(Request(HttpMethod.Get, $"/api/v1/users?search={marker}&page=1&pageSize=2&sort=-fullNameEn", token));

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            var page = (await response.Content.ReadFromJsonAsync<PagedResponse<UserResponse>>(Json))!;
            page.Page.ShouldBe(1);
            page.PageSize.ShouldBe(2);
            page.TotalCount.ShouldBe(3);
            page.TotalPages.ShouldBe(2);
            page.Items.Count.ShouldBe(2);
            page.Items[0].FullNameEn.ShouldEndWith(" 2");

            var invalidSort = await client.SendAsync(Request(HttpMethod.Get, "/api/v1/users?sort=passwordHash", token));
            invalidSort.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }
    }

    [Fact]
    public async Task Get_UnknownUser_Returns404Problem()
    {
        var (client, token, _) = await AdminClientAsync();
        using (client)
        {
            var response = await client.SendAsync(Request(HttpMethod.Get, $"/api/v1/users/{Guid.NewGuid()}", token));

            response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await response.Content.ReadFromJsonAsync<ProblemDetails>(Json))!.Title.ShouldBe("users.not_found");
        }
    }

    [Fact]
    public async Task Update_AmendsNamesAndEmail()
    {
        var (client, token, _) = await AdminClientAsync();
        using (client)
        {
            var user = await SeedUserAsync(factory, roles: RoleNames.Learner);

            var response = await client.SendAsync(Request(HttpMethod.Put, $"/api/v1/users/{user.Id}", token,
                new UpdateUserRequest($"changed_{user.UserName}@example.org", "Changed Name", "اسم معدل")));

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            var updated = (await response.Content.ReadFromJsonAsync<UserResponse>(Json))!;
            updated.Email.ShouldBe($"changed_{user.UserName}@example.org");
            updated.FullNameEn.ShouldBe("Changed Name");
            updated.ModifiedAtUtc.ShouldNotBeNull();
        }
    }

    // SEC-30: deactivation is audited; SEC-08: its sessions are revoked; the account can no longer authenticate.
    [Fact]
    public async Task Deactivate_Returns204_RevokesSessions_AuditsAndBlocksLogin()
    {
        var (client, token, adminId) = await AdminClientAsync();
        using (client)
        {
            var (victim, _, victimRefresh) = await LoginAsRoleAsync(factory, client, RoleNames.Learner);

            var response = await client.SendAsync(Request(HttpMethod.Delete, $"/api/v1/users/{victim.Id}", token));

            response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await ReloadUserAsync(factory, victim.Id)).IsActive.ShouldBeFalse();
            (await RefreshAsync(client, victimRefresh)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            (await LoginAsync(client, victim.UserName, DefaultPassword)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

            var deactivated = (await AuditEventsAsync(factory, adminId, AuditEventTypes.UserDeactivated)).Single(a => a.EntityId == victim.Id);
            deactivated.EntityName.ShouldBe("User");
            (await AuditEventsAsync(factory, adminId, AuditEventTypes.SessionRevoked)).ShouldContain(a => a.DetailsJson!.Contains(victim.Id.ToString()));

            // Reactivation restores access.
            (await client.SendAsync(Request(HttpMethod.Post, $"/api/v1/users/{victim.Id}/activate", token))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
            (await LoginAsync(client, victim.UserName, DefaultPassword)).StatusCode.ShouldBe(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task AssignRoles_ReplacesRoleSet_AndAuditsEachChange()
    {
        var (client, token, adminId) = await AdminClientAsync();
        using (client)
        {
            var user = await SeedUserAsync(factory, roles: RoleNames.Learner);

            var response = await client.SendAsync(Request(HttpMethod.Put, $"/api/v1/users/{user.Id}/roles", token,
                new AssignRolesRequest([RoleNames.Instructor, RoleNames.Registrar])));

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            var updated = (await response.Content.ReadFromJsonAsync<UserResponse>(Json))!;
            updated.Roles.ShouldBe([RoleNames.Instructor, RoleNames.Registrar]);

            (await AuditEventsAsync(factory, adminId, AuditEventTypes.RoleRemoved)).ShouldContain(a => a.EntityId == user.Id && a.DetailsJson!.Contains(RoleNames.Learner));
            (await AuditEventsAsync(factory, adminId, AuditEventTypes.RoleAssigned)).Count(a => a.EntityId == user.Id).ShouldBe(2);

            // The new permissions take effect on the next login.
            var (login, _) = await LoginExpectingSessionAsync(client, user.UserName, DefaultPassword);
            login.Authenticated!.Principal.Permissions.ShouldContain(Permissions.Lms.SubmissionGrade);
        }
    }

    // SDD 15.3: enumeration and revocation of a user's active sessions. SEC-08: immediate effect on refresh.
    [Fact]
    public async Task Sessions_CanBeEnumeratedAndRevokedByAdministrator()
    {
        var (client, token, adminId) = await AdminClientAsync();
        using (client)
        {
            var (subject, _, refreshA) = await LoginAsRoleAsync(factory, client, RoleNames.Learner);
            var (_, refreshB) = await LoginExpectingSessionAsync(client, subject.UserName, DefaultPassword);

            var list = await client.SendAsync(Request(HttpMethod.Get, $"/api/v1/users/{subject.Id}/sessions", token));
            list.StatusCode.ShouldBe(HttpStatusCode.OK);
            var sessions = (await list.Content.ReadFromJsonAsync<List<SessionResponse>>(Json))!;
            sessions.Count.ShouldBe(2);

            var revokeOne = await client.SendAsync(Request(HttpMethod.Delete, $"/api/v1/users/{subject.Id}/sessions/{sessions[0].Id}", token));
            revokeOne.StatusCode.ShouldBe(HttpStatusCode.NoContent);

            var remaining = (await (await client.SendAsync(Request(HttpMethod.Get, $"/api/v1/users/{subject.Id}/sessions", token)))
                .Content.ReadFromJsonAsync<List<SessionResponse>>(Json))!;
            remaining.Count.ShouldBe(1);

            var revokeAll = await client.SendAsync(Request(HttpMethod.Delete, $"/api/v1/users/{subject.Id}/sessions", token));
            revokeAll.StatusCode.ShouldBe(HttpStatusCode.NoContent);

            (await RefreshAsync(client, refreshA)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            (await RefreshAsync(client, refreshB)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            (await AuditEventsAsync(factory, adminId, AuditEventTypes.SessionRevoked)).Count(a => a.DetailsJson!.Contains(subject.Id.ToString())).ShouldBe(2);

            var unknownSession = await client.SendAsync(Request(HttpMethod.Delete, $"/api/v1/users/{subject.Id}/sessions/{Guid.NewGuid()}", token));
            unknownSession.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }
    }

    // SEC-32: the audit trail is readable (with permission) but exposes no write or delete operation.
    [Fact]
    public async Task Audit_IsReadableWithPermission_AndOffersNoMutation()
    {
        var (client, token, adminId) = await AdminClientAsync();
        using (client)
        {
            var response = await client.SendAsync(Request(HttpMethod.Get, $"/api/v1/audit?userId={adminId}&eventType={AuditEventTypes.AuthenticationSucceeded}&pageSize=5", token));

            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            var page = (await response.Content.ReadFromJsonAsync<PagedResponse<AuditEventResponse>>(Json))!;
            page.Items.ShouldNotBeEmpty();
            page.Items.ShouldAllBe(a => a.UserId == adminId && a.EventType == AuditEventTypes.AuthenticationSucceeded);

            foreach (var method in new[] { HttpMethod.Post, HttpMethod.Put, HttpMethod.Delete })
            {
                var mutation = await client.SendAsync(Request(method, $"/api/v1/audit/{page.Items[0].Id}", token, new { }));
                mutation.StatusCode.ShouldBeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
            }
        }
    }
}
