using System.Net;
using System.Net.Http.Json;
using OpenCampus.Identity.Application.Authorization;
using OpenCampus.IntegrationTests.Identity;
using OpenCampus.SharedKernel;
using OpenCampus.Sis.Application.Catalogue;
using OpenCampus.Sis.Application.Enrolments;
using OpenCampus.Sis.Application.Learners;
using OpenCampus.Sis.Application.Programmes;
using OpenCampus.Sis.Domain.Enrolments;
using OpenCampus.Sis.Domain.Learners;
using static OpenCampus.IntegrationTests.Sis.SisTestSupport;

namespace OpenCampus.IntegrationTests.Sis;

/// <summary>Deny-by-default and permission policies on the SIS interface (SEC-10, SEC-11) and learner-scoped self-service (SEC-12, API-06).</summary>
[Collection(ApiCollection.Name)]
public class SisAuthorizationAndSelfServiceTests(ApiFactory factory)
{
    [Fact]
    public async Task SisEndpoints_RequireAuthentication_AndTheDeclaredPermission()
    {
        using var anonymous = factory.CreateApiClient();
        (await anonymous.GetAsync("/api/v1/programmes")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync("/api/v1/catalogue")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var (registrar, _) = await ClientAsAsync(factory, RoleNames.Registrar);
        var (learner, _) = await ClientAsAsync(factory, RoleNames.Learner);
        var request = new CreateProgrammeRequest(Unique("P"), "Forbidden", "ممنوع", 12);

        // Registrar reads the structure but may not author it; Learner may not author anything.
        (await registrar.GetAsync("/api/v1/programmes")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await registrar.PostAsJsonAsync("/api/v1/programmes", request, Json)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await learner.PostAsJsonAsync("/api/v1/programmes", request, Json)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await learner.GetAsync("/api/v1/learners")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await learner.GetAsync("/api/v1/sections/instructors")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Registrar_CanAdministerLearnersAndEnrolments()
    {
        var (admin, _) = await ClientAsAsync(factory, RoleNames.Administrator);
        var (registrar, _) = await ClientAsAsync(factory, RoleNames.Registrar);
        var instructor = await AuthTestSupport.SeedUserAsync(factory, roles: RoleNames.Instructor);
        var course = await CreateCourseAsync(admin, (await CreateProgrammeAsync(admin)).Id);
        var section = await CreateSectionAsync(admin, course.Id, instructor.Id, capacity: 5);
        var (learner, _) = await CreateLearnerAsync(factory, registrar);

        var enrolment = await ReadAsync<EnrolmentResponse>(await EnrolAsync(registrar, learner.Id, section.Section.Id), HttpStatusCode.Created);
        (await registrar.DeleteAsync($"/api/v1/enrolments/{enrolment.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await registrar.PostAsync($"/api/v1/sections/{section.Section.Id}/close", null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Learner_BrowsesOpenCatalogue_EnrolsAndWithdraws_WithinOwnRecordOnly()
    {
        var (admin, _) = await ClientAsAsync(factory, RoleNames.Administrator);
        var instructor = await AuthTestSupport.SeedUserAsync(factory, roles: RoleNames.Instructor);
        var programme = await CreateProgrammeAsync(admin);
        var course = await CreateCourseAsync(admin, programme.Id);
        var open = await CreateSectionAsync(admin, course.Id, instructor.Id, capacity: 3);
        var draft = await CreateSectionAsync(admin, course.Id, instructor.Id, capacity: 3, open: false);

        // A learner-role account with its learner record, signed in as itself.
        var (learnerRecord, learnerUser) = await CreateLearnerAsync(factory, admin);
        using var me = factory.CreateApiClient();
        var (login, _) = await AuthTestSupport.LoginExpectingSessionAsync(me, learnerUser.UserName, AuthTestSupport.DefaultPassword);
        me.DefaultRequestHeaders.Authorization = new("Bearer", login.Authenticated!.AccessToken);

        // Catalogue: only Open sections of the programme; the draft is neither listed nor retrievable.
        var catalogue = await ReadAsync<PagedResponse<CatalogueEntry>>(await me.GetAsync($"/api/v1/catalogue?programmeId={programme.Id}"));
        catalogue.Items.Select(e => e.SectionId).ShouldBe([open.Section.Id]);
        catalogue.Items[0].IsEnrolled.ShouldBeFalse();
        catalogue.Items[0].PlacesRemaining.ShouldBe(3);
        (await me.GetAsync($"/api/v1/catalogue/{draft.Section.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Self-enrolment, then the enrolled listing reflects it; enrolling in the draft is 404, twice is BR-02.
        var enrolment = await ReadAsync<EnrolmentResponse>(await me.PostAsJsonAsync("/api/v1/me/enrolments", new SelfEnrolRequest(open.Section.Id), Json), HttpStatusCode.Created);
        enrolment.Learner.LearnerId.ShouldBe(learnerRecord.Id);
        (await me.PostAsJsonAsync("/api/v1/me/enrolments", new SelfEnrolRequest(draft.Section.Id), Json)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        await (await me.PostAsJsonAsync("/api/v1/me/enrolments", new SelfEnrolRequest(open.Section.Id), Json)).ShouldBeRuleViolationAsync("BR-02");

        var mine = await ReadAsync<PagedResponse<EnrolmentResponse>>(await me.GetAsync("/api/v1/me/enrolments?activeOnly=true"));
        mine.Items.ShouldHaveSingleItem().Id.ShouldBe(enrolment.Id);
        (await ReadAsync<PagedResponse<CatalogueEntry>>(await me.GetAsync($"/api/v1/catalogue?programmeId={programme.Id}"))).Items[0].IsEnrolled.ShouldBeTrue();

        // SEC-12 / API-06: another learner's enrolment is not visible to this learner, so withdrawing it is 404.
        var (other, _) = await CreateLearnerAsync(factory, admin);
        var othersEnrolment = await ReadAsync<EnrolmentResponse>(await EnrolAsync(admin, other.Id, open.Section.Id), HttpStatusCode.Created);
        (await me.DeleteAsync($"/api/v1/me/enrolments/{othersEnrolment.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ReadAsync<EnrolmentResponse>(await admin.GetAsync($"/api/v1/enrolments/{othersEnrolment.Id}"))).Status.ShouldBe(EnrolmentStatus.Active);

        // Own withdrawal succeeds; transcript is the learner's own.
        (await me.DeleteAsync($"/api/v1/me/enrolments/{enrolment.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var transcript = await ReadAsync<TranscriptResponse>(await me.GetAsync("/api/v1/me/enrolments/transcript"));
        transcript.LearnerId.ShouldBe(learnerRecord.Id);
        transcript.Enrolments.ShouldHaveSingleItem().Status.ShouldBe(EnrolmentStatus.Withdrawn);

        // The administrative enrolment interface is not available to the learner, even for its own record (capability, not scope).
        (await me.GetAsync($"/api/v1/learners/{learnerRecord.Id}/enrolments")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await me.GetAsync($"/api/v1/enrolments?learnerId={other.Id}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await me.GetAsync($"/api/v1/enrolments/{othersEnrolment.Id}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await me.GetAsync($"/api/v1/sections/{open.Section.Id}/enrolments")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task LearnerAccountWithoutLearnerRecord_GetsNotFoundFromSelfService()
    {
        var (learner, _) = await ClientAsAsync(factory, RoleNames.Learner);

        (await learner.GetAsync("/api/v1/me/enrolments")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await learner.PostAsJsonAsync("/api/v1/me/enrolments", new SelfEnrolRequest(Guid.NewGuid()), Json)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        // The catalogue itself remains browsable.
        (await learner.GetAsync("/api/v1/catalogue")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SuspendedLearner_CannotEnrol()
    {
        var (admin, _) = await ClientAsAsync(factory, RoleNames.Administrator);
        var instructor = await AuthTestSupport.SeedUserAsync(factory, roles: RoleNames.Instructor);
        var course = await CreateCourseAsync(admin, (await CreateProgrammeAsync(admin)).Id);
        var section = await CreateSectionAsync(admin, course.Id, instructor.Id);
        var (learner, _) = await CreateLearnerAsync(factory, admin);

        await ReadAsync<LearnerResponse>(await admin.PutAsJsonAsync($"/api/v1/learners/{learner.Id}",
            new UpdateLearnerRequest(null, null, Gender.Unspecified, null, LearnerStatus.Suspended), Json));

        (await EnrolAsync(admin, learner.Id, section.Section.Id)).StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }
}
