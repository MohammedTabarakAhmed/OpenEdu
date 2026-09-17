using System.Net;
using System.Net.Http.Json;
using OpenCampus.Identity.Application.Authorization;
using OpenCampus.IntegrationTests.Sis;
using OpenCampus.Lms.Application.Content;
using OpenCampus.Lms.Domain.Content;
using static OpenCampus.IntegrationTests.Lms.LmsTestSupport;

namespace OpenCampus.IntegrationTests.Lms;

/// <summary>
/// Increment 4 exit criteria and the security acceptance criteria they map to, through the interface against
/// the database: authorised access demonstrated; unauthorised access to non-assigned (AC-18) and
/// non-enrolled (AC-19) resources refused; upload controls verified (AC-21); BR-15 refusal (TST-03).
/// </summary>
[Collection(ApiCollection.Name)]
public class ContentAuthorizationTests(ApiFactory factory)
{
    // ----- AC-18: an instructor cannot access work belonging to a section they are not assigned to -----

    [Fact]
    public async Task AC18_NonAssignedInstructor_CannotReadManageOrDownload_AndExistenceIsNotDisclosed()
    {
        var cast = await BuildSectionCastAsync(factory);
        var (unit, item, resource) = await PublishedFileAsync(cast.Instructor, cast.SectionId);
        var other = cast.OtherInstructor;

        // Reading: the section, the unit and the resource all answer 404 (API-06), not 403.
        (await other.GetAsync($"/api/v1/sections/{cast.SectionId}/content")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await other.GetAsync($"/api/v1/content/{unit.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await other.GetAsync($"/api/v1/resources/{resource.Id}/download")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Managing: every mutation is refused the same way and nothing changes.
        (await other.PostAsJsonAsync($"/api/v1/sections/{cast.SectionId}/content", new CreateCourseContentRequest("Hijack", "ع", null), SisTestSupport.Json)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await other.PutAsJsonAsync($"/api/v1/content/{unit.Id}", new UpdateCourseContentRequest("Hijack", "ع", 0), SisTestSupport.Json)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await other.PostAsJsonAsync($"/api/v1/content/{unit.Id}/items", new CreateContentItemRequest("X", "س", ContentItemType.Page, "b", null), SisTestSupport.Json)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await other.PostAsync($"/api/v1/content/{unit.Id}/items/{item.Id}/unpublish", null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await UploadAsync(other, unit.Id, item.Id, "x.pdf", "x"u8.ToArray())).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await other.DeleteAsync($"/api/v1/content/{unit.Id}/items/{item.Id}/resources/{resource.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await other.DeleteAsync($"/api/v1/content/{unit.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // The assigned instructor still sees everything intact.
        var view = await SisTestSupport.ReadAsync<SectionContentResponse>(await cast.Instructor.GetAsync($"/api/v1/sections/{cast.SectionId}/content"));
        view.Contents.Single().TitleEn.ShouldBe("Week 1");
        view.Contents.Single().Items.Single().IsPublished.ShouldBeTrue();
        view.Contents.Single().Items.Single().Resources.ShouldHaveSingleItem();

        // The other instructor's own listing does not include the section.
        var teaching = await SisTestSupport.ReadAsync<List<OpenCampus.Lms.Application.Abstractions.SectionSummary>>(await other.GetAsync("/api/v1/me/sections/teaching"));
        teaching.ShouldNotContain(s => s.Id == cast.SectionId);
    }

    // ----- AC-19: a learner cannot access another learner's records / a section they are not enrolled in -----

    [Fact]
    public async Task AC19_LearnerNotEnrolled_CannotReadOrDownload_AndEnrolledLearnerCannotManage()
    {
        var cast = await BuildSectionCastAsync(factory);
        var (unit, item, resource) = await PublishedFileAsync(cast.Instructor, cast.SectionId);

        // Enrolled elsewhere: 404 everywhere (API-06).
        (await cast.OtherLearner.GetAsync($"/api/v1/sections/{cast.SectionId}/content")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await cast.OtherLearner.GetAsync($"/api/v1/content/{unit.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await cast.OtherLearner.GetAsync($"/api/v1/resources/{resource.Id}/download")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var enrolled = await SisTestSupport.ReadAsync<List<OpenCampus.Lms.Application.Abstractions.SectionSummary>>(await cast.OtherLearner.GetAsync("/api/v1/me/sections/enrolled"));
        enrolled.ShouldNotContain(s => s.Id == cast.SectionId);

        // Enrolled here: may read and download, but holds no write capability at all (SEC-11: 403 at the policy).
        (await cast.Learner.GetAsync($"/api/v1/content/{unit.Id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await cast.Learner.GetAsync($"/api/v1/resources/{resource.Id}/download")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await cast.Learner.PostAsJsonAsync($"/api/v1/sections/{cast.SectionId}/content", new CreateCourseContentRequest("X", "س", null), SisTestSupport.Json)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await cast.Learner.PostAsync($"/api/v1/content/{unit.Id}/items/{item.Id}/unpublish", null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await UploadAsync(cast.Learner, unit.Id, item.Id, "x.pdf", "x"u8.ToArray())).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await cast.Learner.DeleteAsync($"/api/v1/content/{unit.Id}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await cast.Learner.GetAsync("/api/v1/me/sections/teaching")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task AC19_WithdrawnLearner_LosesAccess()
    {
        var cast = await BuildSectionCastAsync(factory);
        var (_, _, resource) = await PublishedFileAsync(cast.Instructor, cast.SectionId);
        (await cast.Learner.GetAsync($"/api/v1/resources/{resource.Id}/download")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // The administrator withdraws the enrolment through SIS; the LMS learns of it through the contract on the next request.
        var enrolments = await SisTestSupport.ReadAsync<OpenCampus.SharedKernel.PagedResponse<OpenCampus.Sis.Application.Enrolments.EnrolmentResponse>>(
            await cast.Admin.GetAsync($"/api/v1/learners/{cast.LearnerRecord.Id}/enrolments"));
        var enrolment = enrolments.Items.Single(e => e.Section.SectionId == cast.SectionId);
        (await cast.Admin.DeleteAsync($"/api/v1/enrolments/{enrolment.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await cast.Learner.GetAsync($"/api/v1/sections/{cast.SectionId}/content")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await cast.Learner.GetAsync($"/api/v1/resources/{resource.Id}/download")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // ----- BR-15: content is not visible to a learner until published -----

    [Fact]
    public async Task BR15_UnpublishedItem_IsInvisibleToLearner_UntilPublished_AndHiddenAgainOnUnpublish()
    {
        var cast = await BuildSectionCastAsync(factory);
        var unit = await CreateUnitAsync(cast.Instructor, cast.SectionId);
        var fileItem = await AddItemAsync(cast.Instructor, unit.Id, ContentItemType.File, null);
        var resource = await UploadExpectingCreatedAsync(cast.Instructor, unit.Id, fileItem.Id, "draft.pdf", "%PDF"u8.ToArray());

        // Unpublished: the listing hides the item and its resource cannot be downloaded (refused as not found).
        var before = await SisTestSupport.ReadAsync<CourseContentResponse>(await cast.Learner.GetAsync($"/api/v1/content/{unit.Id}"));
        before.Items.ShouldBeEmpty();
        (await cast.Learner.GetAsync($"/api/v1/resources/{resource.Id}/download")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        // …while the manager can already retrieve it.
        (await cast.Instructor.GetAsync($"/api/v1/resources/{resource.Id}/download")).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await cast.Instructor.PostAsync($"/api/v1/content/{unit.Id}/items/{fileItem.Id}/publish", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var after = await SisTestSupport.ReadAsync<CourseContentResponse>(await cast.Learner.GetAsync($"/api/v1/content/{unit.Id}"));
        after.Items.Single().Id.ShouldBe(fileItem.Id);
        (await cast.Learner.GetAsync($"/api/v1/resources/{resource.Id}/download")).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await cast.Instructor.PostAsync($"/api/v1/content/{unit.Id}/items/{fileItem.Id}/unpublish", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await cast.Learner.GetAsync($"/api/v1/resources/{resource.Id}/download")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // ----- AC-21: upload controls SEC-22 to SEC-26 -----

    [Theory]
    [InlineData("payload.exe", "application/octet-stream")]
    [InlineData("script.js", "text/javascript")]
    [InlineData("page.html", "text/html")]
    [InlineData("noextension", "application/octet-stream")]
    [InlineData("notes.pdf.exe", "application/octet-stream")]
    public async Task AC21_UploadWithExtensionOffTheAllowList_IsRefused_SEC22(string fileName, string contentType)
    {
        var cast = await BuildSectionCastAsync(factory);
        var unit = await CreateUnitAsync(cast.Instructor, cast.SectionId);
        var item = await AddItemAsync(cast.Instructor, unit.Id, ContentItemType.File, null);

        var response = await UploadAsync(cast.Instructor, unit.Id, item.Id, fileName, "MZ..."u8.ToArray(), contentType);

        await response.ShouldBeFieldValidationErrorAsync("File");
        var view = await SisTestSupport.ReadAsync<CourseContentResponse>(await cast.Instructor.GetAsync($"/api/v1/content/{unit.Id}"));
        view.Items.Single().Resources.ShouldBeEmpty();
        var directory = Path.Combine(factory.FileStoreRoot, "content", cast.SectionId.ToString("N"), item.Id.ToString("N"));
        (Directory.Exists(directory) ? Directory.GetFiles(directory) : []).ShouldBeEmpty();
    }

    [Fact]
    public async Task AC21_EmptyUpload_IsRefused()
    {
        var cast = await BuildSectionCastAsync(factory);
        var unit = await CreateUnitAsync(cast.Instructor, cast.SectionId);
        var item = await AddItemAsync(cast.Instructor, unit.Id, ContentItemType.File, null);

        var response = await UploadAsync(cast.Instructor, unit.Id, item.Id, "empty.pdf", []);

        await response.ShouldBeFieldValidationErrorAsync("File");
    }

    [Fact]
    public async Task AC21_StoredNameIsSystemGenerated_ClientNameIsDisplayOnly_SEC23()
    {
        var cast = await BuildSectionCastAsync(factory);
        var unit = await CreateUnitAsync(cast.Instructor, cast.SectionId);
        var item = await AddItemAsync(cast.Instructor, unit.Id, ContentItemType.File, null);

        // A hostile client name: traversal, a directory, and a second extension. Only the leaf is kept for display.
        var resource = await UploadExpectingCreatedAsync(cast.Instructor, unit.Id, item.Id, "..\\..\\evil\\..\\Report Final (v2).pdf", "%PDF"u8.ToArray());

        resource.FileName.ShouldBe("Report Final (v2).pdf");
        var directory = Path.Combine(factory.FileStoreRoot, "content", cast.SectionId.ToString("N"), item.Id.ToString("N"));
        var stored = Path.GetFileName(Directory.GetFiles(directory).Single());
        stored.ShouldMatch("^[0-9a-f]{32}\\.pdf$");
        Directory.Exists(Path.Combine(factory.FileStoreRoot, "evil")).ShouldBeFalse();
        Directory.Exists(Path.Combine(factory.FileStoreRoot, "..", "evil")).ShouldBeFalse();
    }

    [Fact]
    public async Task AC21_DirectRetrievalWithoutAuthorisation_IsRefused_SEC24_SEC25()
    {
        var cast = await BuildSectionCastAsync(factory);
        var (_, _, resource) = await PublishedFileAsync(cast.Instructor, cast.SectionId);

        // Anonymous: the authorised endpoint demands a credential (SEC-10).
        (await factory.CreateApiClient().GetAsync($"/api/v1/resources/{resource.Id}/download")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        // Authenticated but without the content capability (Registrar holds no lms.* code): 403 at the policy (SEC-11).
        var (registrar, _) = await SisTestSupport.ClientAsAsync(factory, RoleNames.Registrar);
        (await registrar.GetAsync($"/api/v1/resources/{resource.Id}/download")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Capability without scope: 404 (SEC-12, API-06).
        (await cast.OtherLearner.GetAsync($"/api/v1/resources/{resource.Id}/download")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AC21_RemovingAResource_DeletesTheStoredFile_AndRefusesLaterDownloads()
    {
        var cast = await BuildSectionCastAsync(factory);
        var (unit, item, resource) = await PublishedFileAsync(cast.Instructor, cast.SectionId);
        var directory = Path.Combine(factory.FileStoreRoot, "content", cast.SectionId.ToString("N"), item.Id.ToString("N"));
        Directory.GetFiles(directory).ShouldHaveSingleItem();

        (await cast.Instructor.DeleteAsync($"/api/v1/content/{unit.Id}/items/{item.Id}/resources/{resource.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        Directory.GetFiles(directory).ShouldBeEmpty();
        (await cast.Instructor.GetAsync($"/api/v1/resources/{resource.Id}/download")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await cast.Learner.GetAsync($"/api/v1/resources/{resource.Id}/download")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // ----- Deny by default and the policy layer for the new routes (SEC-10, SEC-11) -----

    [Fact]
    public async Task ContentRoutes_RequireCredentialAndCapability()
    {
        var cast = await BuildSectionCastAsync(factory);
        var anonymous = factory.CreateApiClient();

        (await anonymous.GetAsync($"/api/v1/sections/{cast.SectionId}/content")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync("/api/v1/me/sections/enrolled")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var (registrar, _) = await SisTestSupport.ClientAsAsync(factory, RoleNames.Registrar);
        (await registrar.GetAsync($"/api/v1/sections/{cast.SectionId}/content")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await registrar.GetAsync("/api/v1/me/sections/enrolled")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
