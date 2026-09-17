using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using OpenCampus.Identity.Infrastructure.Persistence;
using OpenCampus.Identity.Infrastructure.Security;
using OpenCampus.IntegrationTests.Lms;
using OpenCampus.SharedKernel;
using OpenCampus.Sis.Application.Abstractions;
using OpenCampus.Sis.Application.Catalogue;
using OpenCampus.Sis.Application.Grading;
using OpenCampus.Sis.Application.Sections;
using OpenCampus.Sis.Domain.Enrolments;

namespace OpenCampus.IntegrationTests.Sis;

/// <summary>
/// Grading, release and learner results through the application services against the database, with the caller
/// impersonated on the request context: BR-05/BR-06/BR-07 in flight, SEC-12 scope, SEC-30 audit records.
/// </summary>
[Collection(ApiCollection.Name)]
public class GradingServiceTests(ApiFactory factory)
{
    private static IServiceScope ScopeAs(ApiFactory factory, Guid userId, params string[] permissions)
    {
        var scope = factory.Services.CreateScope();
        var claims = new List<Claim> { new(JwtRegisteredClaimNames.Sub, userId.ToString()) };
        claims.AddRange(permissions.Select(p => new Claim(JwtAccessTokenIssuer.PermissionClaim, p)));
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext =
            new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) };
        return scope;
    }

    private static async Task<(SectionCast Cast, SectionDetailResponse Section, Guid EnrolmentId)> ArrangeAsync(ApiFactory factory)
    {
        var cast = await LmsTestSupport.BuildSectionCastAsync(factory);
        var section = await SisTestSupport.ReadAsync<SectionDetailResponse>(await cast.Admin.GetAsync($"/api/v1/sections/{cast.SectionId}"));
        var enrolments = await SisTestSupport.ReadAsync<PagedResponse<OpenCampus.Sis.Application.Enrolments.EnrolmentResponse>>(
            await cast.Admin.GetAsync($"/api/v1/sections/{cast.SectionId}/enrolments"));
        return (cast, section, enrolments.Items.Single().Id);
    }

    [Fact]
    public async Task Instructor_RecordsAndAmends_WithAuditRecords_ThenReleases()
    {
        var (cast, section, enrolmentId) = await ArrangeAsync(factory);
        var component = section.GradeComponents.Single(); // 100% of 100
        GradeEntryResponse entry;

        using (var scope = ScopeAs(factory, cast.InstructorUser.Id))
        {
            var grading = scope.ServiceProvider.GetRequiredService<GradingService>();

            var created = await grading.RecordGradeAsync(cast.SectionId, new RecordGradeRequest(enrolmentId, component.Id, 70m), CancellationToken.None);
            created.IsSuccess.ShouldBeTrue(created.IsFailure ? created.Error.Message : null);
            created.Value.GradedByUserId.ShouldBe(cast.InstructorUser.Id);

            var amended = await grading.RecordGradeAsync(cast.SectionId, new RecordGradeRequest(enrolmentId, component.Id, 82m), CancellationToken.None);
            amended.Value.Id.ShouldBe(created.Value.Id);
            amended.Value.Score.ShouldBe(82m);
            entry = amended.Value;

            // BR-05 through the service: refused by the aggregate as 422 material.
            var violation = await Should.ThrowAsync<BusinessRuleViolationException>(() =>
                grading.RecordGradeAsync(cast.SectionId, new RecordGradeRequest(enrolmentId, component.Id, 100.5m), CancellationToken.None));
            violation.RuleCode.ShouldBe("BR-05");

            var book = (await grading.GetGradebookAsync(cast.SectionId, CancellationToken.None)).Value;
            book.Rows.Single().Entries.Single().Score.ShouldBe(82m);
            book.UngradedPairCount.ShouldBe(0);
            book.IsReleased.ShouldBeFalse();

            var released = (await grading.ReleaseAsync(cast.SectionId, CancellationToken.None)).Value;
            released.IsReleased.ShouldBeTrue();
            released.Rows.Single().Status.ShouldBe(EnrolmentStatus.Completed);
            released.Rows.Single().FinalGrade.ShouldBe(82m);
        }

        // SEC-30: creation, amendment and release each left an immutable record with actor and entity.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var events = await db.AuditEvents.Where(e => e.UserId == cast.InstructorUser.Id).OrderBy(e => e.OccurredAtUtc).ToListAsync();
            events.Select(e => e.EventType).ShouldContain(SisAuditEventTypes.GradeCreated);
            events.Select(e => e.EventType).ShouldContain(SisAuditEventTypes.GradeAmended);
            events.Select(e => e.EventType).ShouldContain(SisAuditEventTypes.GradesReleased);
            events.Single(e => e.EventType == SisAuditEventTypes.GradeAmended).EntityId.ShouldBe(entry.Id);
            events.Single(e => e.EventType == SisAuditEventTypes.GradesReleased).EntityId.ShouldBe(cast.SectionId);
        }
    }

    [Fact]
    public async Task BR07_Release_IsRefusedWhileAnyActiveEnrolmentIsUngraded()
    {
        var (cast, _, _) = await ArrangeAsync(factory);
        using var scope = ScopeAs(factory, cast.InstructorUser.Id);
        var grading = scope.ServiceProvider.GetRequiredService<GradingService>();

        var violation = await Should.ThrowAsync<BusinessRuleViolationException>(() => grading.ReleaseAsync(cast.SectionId, CancellationToken.None));

        violation.RuleCode.ShouldBe("BR-07");
        (await grading.GetGradebookAsync(cast.SectionId, CancellationToken.None)).Value.UngradedPairCount.ShouldBe(1);
    }

    [Fact]
    public async Task SEC12_OnlyTheAssignedInstructorOrAnAdministrator_MayGrade()
    {
        var (cast, section, enrolmentId) = await ArrangeAsync(factory);
        var component = section.GradeComponents.Single();
        var request = new RecordGradeRequest(enrolmentId, component.Id, 50m);
        var otherInstructor = await Identity.AuthTestSupport.SeedUserAsync(factory, roles: OpenCampus.Identity.Application.Authorization.RoleNames.Instructor);

        using (var scope = ScopeAs(factory, otherInstructor.Id, "sis.grade.write"))
        {
            var grading = scope.ServiceProvider.GetRequiredService<GradingService>();
            (await grading.GetGradebookAsync(cast.SectionId, CancellationToken.None)).Error.Type.ShouldBe(ErrorType.NotFound);
            (await grading.RecordGradeAsync(cast.SectionId, request, CancellationToken.None)).Error.Type.ShouldBe(ErrorType.NotFound);
            (await grading.ReleaseAsync(cast.SectionId, CancellationToken.None)).Error.Type.ShouldBe(ErrorType.NotFound);
        }

        using (var scope = ScopeAs(factory, Guid.NewGuid(), "sis.grade.write", GradingService.SectionAdministration))
        {
            var grading = scope.ServiceProvider.GetRequiredService<GradingService>();
            (await grading.RecordGradeAsync(cast.SectionId, request, CancellationToken.None)).IsSuccess.ShouldBeTrue();
        }
    }

    [Fact]
    public async Task BR06_Learner_SeesNoGradeBeforeRelease_AndOnlyTheirOwnAfter()
    {
        var (cast, section, enrolmentId) = await ArrangeAsync(factory);
        var component = section.GradeComponents.Single();

        using (var scope = ScopeAs(factory, cast.InstructorUser.Id))
        {
            (await scope.ServiceProvider.GetRequiredService<GradingService>()
                .RecordGradeAsync(cast.SectionId, new RecordGradeRequest(enrolmentId, component.Id, 91m), CancellationToken.None)).IsSuccess.ShouldBeTrue();
        }

        using (var scope = ScopeAs(factory, cast.LearnerUser.Id))
        {
            var self = scope.ServiceProvider.GetRequiredService<LearnerSelfService>();
            var before = (await self.MyResultsAsync(enrolmentId, CancellationToken.None)).Value;
            before.Entries.ShouldBeEmpty(); // BR-06: recorded but unreleased
            before.IsReleased.ShouldBeFalse();
            before.FinalGrade.ShouldBeNull();
        }

        using (var scope = ScopeAs(factory, cast.InstructorUser.Id))
        {
            (await scope.ServiceProvider.GetRequiredService<GradingService>().ReleaseAsync(cast.SectionId, CancellationToken.None)).IsSuccess.ShouldBeTrue();
        }

        using (var scope = ScopeAs(factory, cast.LearnerUser.Id))
        {
            var self = scope.ServiceProvider.GetRequiredService<LearnerSelfService>();
            var after = (await self.MyResultsAsync(enrolmentId, CancellationToken.None)).Value;
            after.Entries.Single().Score.ShouldBe(91m);
            after.IsReleased.ShouldBeTrue();
            after.FinalGrade.ShouldBe(91m);
            after.Status.ShouldBe(EnrolmentStatus.Completed);
        }

        using (var scope = ScopeAs(factory, cast.OtherLearnerUser.Id))
        {
            // Another learner's enrolment is indistinguishable from a missing one (SEC-12, API-06).
            var self = scope.ServiceProvider.GetRequiredService<LearnerSelfService>();
            (await self.MyResultsAsync(enrolmentId, CancellationToken.None)).Error.Type.ShouldBe(ErrorType.NotFound);
        }
    }
}
