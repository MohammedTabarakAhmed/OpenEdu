using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenCampus.Api.Persistence;
using OpenCampus.Identity.Application.Administration;
using OpenCampus.Identity.Application.Authorization;
using OpenCampus.Identity.Infrastructure.Persistence;
using static OpenCampus.IntegrationTests.Identity.AuthTestSupport;

namespace OpenCampus.IntegrationTests.Identity;

/// <summary>SDD 13.6 / 18.7 / Appendix C: reference data provisioning, and SEC-10/SEC-11 policy enforcement.</summary>
[Collection(ApiCollection.Name)]
public class ProvisioningAndAuthorizationTests(ApiFactory factory)
{
    [Fact]
    public async Task ReferenceData_ProvisionsRolesPermissionCatalogueAndDefaultMappings()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var roles = await db.Roles.Include(r => r.Permissions).ToListAsync();
        roles.Select(r => r.Name).ShouldBe(
            [RoleNames.Administrator, RoleNames.Instructor, RoleNames.Learner, RoleNames.Registrar], ignoreOrder: true);

        var permissions = await db.Permissions.Select(p => p.Code).ToListAsync();
        permissions.ShouldBe(Permissions.AllCodes.ToList(), ignoreOrder: true);

        var administrator = roles.Single(r => r.Name == RoleNames.Administrator);
        administrator.Permissions.Count.ShouldBe(Permissions.Catalogue.Count, "Administrator holds the full catalogue");

        var learner = roles.Single(r => r.Name == RoleNames.Learner);
        learner.Permissions.Count.ShouldBe(Permissions.DefaultRoleMappings[RoleNames.Learner].Count);
    }

    // SDD 18.7: provisioning is idempotent.
    [Fact]
    public async Task Provisioning_RunTwice_ChangesNothing()
    {
        var contentRoot = factory.Services.GetRequiredService<IWebHostEnvironment>().ContentRootPath;

        await DatabaseInitializer.ProvisionAsync(factory.Services, contentRoot);
        await DatabaseInitializer.ProvisionAsync(factory.Services, contentRoot);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        (await db.Roles.CountAsync()).ShouldBe(4);
        (await db.Permissions.CountAsync()).ShouldBe(Permissions.Catalogue.Count);
        (await db.Set<OpenCampus.Identity.Domain.Roles.RolePermission>().CountAsync())
            .ShouldBe(Permissions.DefaultRoleMappings.Values.Sum(v => v.Count));
    }

    // SEC-10: deny by default — an administrative endpoint is inaccessible anonymously.
    [Fact]
    public async Task Users_WithoutToken_IsUnauthorized()
    {
        using var client = factory.CreateApiClient();

        (await client.GetAsync("/api/v1/users")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await client.GetAsync("/api/v1/roles")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await client.GetAsync("/api/v1/audit")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // SEC-11: policies are bound to permission codes; a role without the code is forbidden.
    [Fact]
    public async Task Users_AsLearner_IsForbidden_AsAdministrator_IsAllowed()
    {
        using var client = factory.CreateApiClient();
        var (_, learnerToken, _) = await LoginAsRoleAsync(factory, client, RoleNames.Learner);
        var (_, adminToken, _) = await LoginAsRoleAsync(factory, client, RoleNames.Administrator);

        var forbidden = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/api/v1/users").WithBearer(learnerToken));
        forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var allowed = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/api/v1/users").WithBearer(adminToken));
        allowed.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Registrar_LacksIdentityPermissions_ButTokenCarriesSisPermissions()
    {
        using var client = factory.CreateApiClient();
        var (_, token, _) = await LoginAsRoleAsync(factory, client, RoleNames.Registrar);

        (await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/api/v1/users").WithBearer(token)))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var me = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me").WithBearer(token));
        var principal = (await me.Content.ReadFromJsonAsync<OpenCampus.Identity.Application.Authentication.PrincipalResponse>(Json))!;
        principal.Roles.ShouldBe([RoleNames.Registrar]);
        principal.Permissions.ShouldContain(Permissions.Sis.EnrolmentWrite);
        principal.Permissions.ShouldNotContain(Permissions.Identity.UserRead);
    }

    [Fact]
    public async Task Roles_ListsCatalogueMappings()
    {
        using var client = factory.CreateApiClient();
        var (_, token, _) = await LoginAsRoleAsync(factory, client, RoleNames.Administrator);

        var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/api/v1/roles").WithBearer(token));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var roles = (await response.Content.ReadFromJsonAsync<List<RoleResponse>>(Json))!;
        roles.Count.ShouldBe(4);
        roles.Single(r => r.Name == RoleNames.Instructor).Permissions.ShouldContain(Permissions.Lms.SubmissionGrade);
    }
}
