using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using OpenCampus.IntegrationTests.Sis;
using OpenCampus.Lms.Application.Abstractions;
using OpenCampus.Lms.Application.Content;
using OpenCampus.Lms.Application.Storage;
using OpenCampus.Lms.Domain.Content;
using OpenCampus.Lms.Infrastructure.Storage;
using static OpenCampus.IntegrationTests.Lms.LmsTestSupport;

namespace OpenCampus.IntegrationTests.Lms;

/// <summary>
/// The content half of AC-03/AC-04 through the interface against the database: the assigned instructor
/// builds and publishes content with an uploaded resource; the enrolled learner reads it and downloads the
/// file. Also the host wiring: the LMS→SIS adapter, the framework upload bound and the absence of static serving.
/// </summary>
[Collection(ApiCollection.Name)]
public class ContentLifecycleTests(ApiFactory factory)
{
    [Fact]
    public async Task Instructor_PublishesContentWithResource_Learner_ReadsAndDownloads()
    {
        var cast = await BuildSectionCastAsync(factory);
        var bytes = new byte[4096];
        RandomNumberGenerator.Fill(bytes);

        // The instructor sees the section among those they teach.
        var teaching = await SisTestSupport.ReadAsync<List<SectionSummary>>(await cast.Instructor.GetAsync("/api/v1/me/sections/teaching"));
        teaching.ShouldContain(s => s.Id == cast.SectionId && s.InstructorUserId == cast.InstructorUser.Id);

        // Build: unit → page (published) → link (draft) → file item with upload (published).
        var unit = await CreateUnitAsync(cast.Instructor, cast.SectionId);
        unit.SectionId.ShouldBe(cast.SectionId);
        var page = await AddItemAsync(cast.Instructor, unit.Id, publish: true);
        var link = await AddItemAsync(cast.Instructor, unit.Id, ContentItemType.Link, "https://example.org/reading");
        var fileItem = await AddItemAsync(cast.Instructor, unit.Id, ContentItemType.File, null);
        var resource = await UploadExpectingCreatedAsync(cast.Instructor, unit.Id, fileItem.Id, "Lecture 1.pdf", bytes);
        resource.FileName.ShouldBe("Lecture 1.pdf");
        resource.SizeBytes.ShouldBe(bytes.Length);
        resource.ContentHash.ShouldBe(Convert.ToHexStringLower(SHA256.HashData(bytes))); // SEC-26
        (await cast.Instructor.PostAsync($"/api/v1/content/{unit.Id}/items/{fileItem.Id}/publish", null)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // The manager's view holds all three; the learner's view holds the two published ones (BR-15).
        var managerView = await SisTestSupport.ReadAsync<SectionContentResponse>(await cast.Instructor.GetAsync($"/api/v1/sections/{cast.SectionId}/content"));
        managerView.CanManage.ShouldBeTrue();
        managerView.Contents.Single().Items.Select(i => i.Id).ShouldBe([page.Id, link.Id, fileItem.Id]);

        var enrolled = await SisTestSupport.ReadAsync<List<SectionSummary>>(await cast.Learner.GetAsync("/api/v1/me/sections/enrolled"));
        enrolled.Select(s => s.Id).ShouldBe([cast.SectionId]);
        var learnerView = await SisTestSupport.ReadAsync<SectionContentResponse>(await cast.Learner.GetAsync($"/api/v1/sections/{cast.SectionId}/content"));
        learnerView.CanManage.ShouldBeFalse();
        learnerView.Contents.Single().Items.Select(i => i.Id).ShouldBe([page.Id, fileItem.Id]);
        learnerView.Contents.Single().Items.Single(i => i.Id == fileItem.Id).Resources.Single().Id.ShouldBe(resource.Id);

        // Download: the bytes, the display name and the digest come back; the stored name never appears (SEC-23).
        var download = await cast.Learner.GetAsync($"/api/v1/resources/{resource.Id}/download");
        download.StatusCode.ShouldBe(HttpStatusCode.OK);
        download.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
        download.Content.Headers.ContentDisposition!.FileNameStar.ShouldBe("Lecture 1.pdf");
        download.Headers.GetValues("X-Content-SHA256").Single().ShouldBe(resource.ContentHash);
        (await download.Content.ReadAsByteArrayAsync()).ShouldBe(bytes);

        // The file lives beneath the configured root in the owner-derived hierarchy (18.5), outside the host's tree.
        var store = (LocalFileStore)factory.Services.GetRequiredService<IFileStore>();
        store.RootPath.ShouldBe(Path.GetFullPath(factory.FileStoreRoot));
        Directory.GetFiles(Path.Combine(store.RootPath, "content", cast.SectionId.ToString("N"), fileItem.Id.ToString("N"))).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Administrator_MayManageAnySection_AndReorderAmendAndDelete()
    {
        var cast = await BuildSectionCastAsync(factory);

        var unit = await CreateUnitAsync(cast.Admin, cast.SectionId, "Week A");
        var first = await AddItemAsync(cast.Admin, unit.Id);
        var second = await AddItemAsync(cast.Admin, unit.Id);

        var reordered = await SisTestSupport.ReadAsync<CourseContentResponse>(await cast.Admin.PutAsJsonAsync(
            $"/api/v1/content/{unit.Id}/items/reorder", new ReorderItemsRequest([second.Id, first.Id]), SisTestSupport.Json));
        reordered.Items.Select(i => i.Id).ShouldBe([second.Id, first.Id]);

        var amended = await SisTestSupport.ReadAsync<ContentItemResponse>(await cast.Admin.PutAsJsonAsync(
            $"/api/v1/content/{unit.Id}/items/{first.Id}", new UpdateContentItemRequest("Renamed", "معاد التسمية", "New body", 7), SisTestSupport.Json));
        amended.TitleEn.ShouldBe("Renamed");
        amended.SortOrder.ShouldBe(7);

        var unitAmended = await SisTestSupport.ReadAsync<CourseContentResponse>(await cast.Admin.PutAsJsonAsync(
            $"/api/v1/content/{unit.Id}", new UpdateCourseContentRequest("Week B", "ب", 3), SisTestSupport.Json));
        unitAmended.TitleEn.ShouldBe("Week B");

        (await cast.Admin.DeleteAsync($"/api/v1/content/{unit.Id}/items/{second.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await cast.Admin.DeleteAsync($"/api/v1/content/{unit.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await cast.Admin.GetAsync($"/api/v1/content/{unit.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Validation_IsFieldKeyed_AndDomainRefusalsAre422()
    {
        var cast = await BuildSectionCastAsync(factory);
        var unit = await CreateUnitAsync(cast.Instructor, cast.SectionId);

        var missingTitle = await cast.Instructor.PostAsJsonAsync($"/api/v1/sections/{cast.SectionId}/content", new CreateCourseContentRequest("", "ع", null), SisTestSupport.Json);
        await missingTitle.ShouldBeFieldValidationErrorAsync("TitleEn");

        var badLink = await cast.Instructor.PostAsJsonAsync($"/api/v1/content/{unit.Id}/items", new CreateContentItemRequest("L", "ر", ContentItemType.Link, "javascript:alert(1)", null), SisTestSupport.Json);
        await badLink.ShouldBeFieldValidationErrorAsync("Body");

        // A file item without a resource cannot be published: domain refusal → 422 (API-07).
        var fileItem = await AddItemAsync(cast.Instructor, unit.Id, ContentItemType.File, null);
        var publish = await cast.Instructor.PostAsync($"/api/v1/content/{unit.Id}/items/{fileItem.Id}/publish", null);
        publish.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Upload_BeyondFrameworkLimit_IsRefusedBeforeTheApplicationLayer()
    {
        var cast = await BuildSectionCastAsync(factory);
        var unit = await CreateUnitAsync(cast.Instructor, cast.SectionId);
        var item = await AddItemAsync(cast.Instructor, unit.Id, ContentItemType.File, null);

        var response = await UploadAsync(cast.Instructor, unit.Id, item.Id, "big.pdf", new byte[ApiFactory.MaxUploadSizeBytes + 1024]);

        // SEC-22 at the framework level: the multipart body limit rejects the request; nothing was stored.
        response.StatusCode.ShouldBeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.RequestEntityTooLarge);
        var view = await SisTestSupport.ReadAsync<SectionContentResponse>(await cast.Instructor.GetAsync($"/api/v1/sections/{cast.SectionId}/content"));
        view.Contents.Single().Items.Single().Resources.ShouldBeEmpty();
        var directory = Path.Combine(factory.FileStoreRoot, "content", cast.SectionId.ToString("N"), item.Id.ToString("N"));
        (Directory.Exists(directory) ? Directory.GetFiles(directory) : []).ShouldBeEmpty();
    }

    [Fact]
    public async Task StoredFiles_AreNotServedStatically_SEC24()
    {
        var cast = await BuildSectionCastAsync(factory);
        var (_, item, _) = await PublishedFileAsync(cast.Instructor, cast.SectionId);
        var directory = Path.Combine(factory.FileStoreRoot, "content", cast.SectionId.ToString("N"), item.Id.ToString("N"));
        var storedName = Path.GetFileName(Directory.GetFiles(directory).Single());

        foreach (var path in new[]
        {
            $"/data/files/content/{cast.SectionId:N}/{item.Id:N}/{storedName}",
            $"/files/content/{cast.SectionId:N}/{item.Id:N}/{storedName}",
            $"/content/{cast.SectionId:N}/{item.Id:N}/{storedName}",
        })
        {
            var anonymous = await factory.CreateApiClient().GetAsync(path);
            anonymous.StatusCode.ShouldBeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Unauthorized);
            var authenticated = await cast.Admin.GetAsync(path);
            authenticated.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }
    }

    [Fact]
    public async Task ProblemFormat_ForMissingFile_IsFieldKeyed()
    {
        var cast = await BuildSectionCastAsync(factory);
        var unit = await CreateUnitAsync(cast.Instructor, cast.SectionId);
        var item = await AddItemAsync(cast.Instructor, unit.Id, ContentItemType.File, null);

        // A well-formed multipart body that carries no "file" part.
        var form = new MultipartFormDataContent { { new StringContent("x"), "other" } };
        var response = await cast.Instructor.PostAsync($"/api/v1/content/{unit.Id}/items/{item.Id}/resources", form);
        await response.ShouldBeFieldValidationErrorAsync("File");

        // An unreadable body is reported in the same problem shape (API-04), not the framework default.
        var malformed = await cast.Instructor.PostAsync($"/api/v1/content/{unit.Id}/items/{item.Id}/resources", new MultipartFormDataContent());
        var body = await malformed.Content.ReadAsStringAsync();
        malformed.StatusCode.ShouldBe(HttpStatusCode.BadRequest, body);
        using var problem = JsonDocument.Parse(body);
        problem.RootElement.GetProperty("type").GetString().ShouldBe("urn:opencampus:error:validation");
        problem.RootElement.GetProperty("errors").TryGetProperty("Request", out _).ShouldBeTrue(body);
    }
}
