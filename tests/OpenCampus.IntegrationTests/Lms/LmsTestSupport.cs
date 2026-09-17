using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using OpenCampus.Identity.Application.Authorization;
using OpenCampus.Identity.Domain.Users;
using OpenCampus.IntegrationTests.Sis;
using OpenCampus.Lms.Application.Content;
using OpenCampus.Lms.Domain.Content;
using OpenCampus.Sis.Application.Learners;

namespace OpenCampus.IntegrationTests.Lms;

/// <summary>
/// A section with everyone SEC-12 cares about: the assigned instructor, another instructor, an enrolled
/// learner, a learner enrolled elsewhere, and the administrator who built it — all through the public interface.
/// </summary>
internal sealed record SectionCast(
    Guid SectionId,
    HttpClient Admin,
    HttpClient Instructor,
    User InstructorUser,
    HttpClient OtherInstructor,
    HttpClient Learner,
    LearnerResponse LearnerRecord,
    User LearnerUser,
    HttpClient OtherLearner,
    User OtherLearnerUser);

internal static class LmsTestSupport
{
    public static async Task<SectionCast> BuildSectionCastAsync(ApiFactory factory, bool enrolLearner = true)
    {
        var (admin, _) = await SisTestSupport.ClientAsAsync(factory, RoleNames.Administrator);
        var (instructor, instructorUser) = await SisTestSupport.ClientAsAsync(factory, RoleNames.Instructor);
        var (otherInstructor, _) = await SisTestSupport.ClientAsAsync(factory, RoleNames.Instructor);

        var programme = await SisTestSupport.CreateProgrammeAsync(admin);
        var course = await SisTestSupport.CreateCourseAsync(admin, programme.Id);
        var section = await SisTestSupport.CreateSectionAsync(admin, course.Id, instructorUser.Id, capacity: 10);

        var (learnerRecord, learnerUser) = await SisTestSupport.CreateLearnerAsync(factory, admin);
        if (enrolLearner)
        {
            (await SisTestSupport.EnrolAsync(admin, learnerRecord.Id, section.Section.Id)).StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        var (otherLearnerRecord, otherLearnerUser) = await SisTestSupport.CreateLearnerAsync(factory, admin);
        var elsewhere = await SisTestSupport.CreateSectionAsync(admin, course.Id, instructorUser.Id, capacity: 10);
        (await SisTestSupport.EnrolAsync(admin, otherLearnerRecord.Id, elsewhere.Section.Id)).StatusCode.ShouldBe(HttpStatusCode.Created);

        return new SectionCast(
            section.Section.Id, admin, instructor, instructorUser, otherInstructor,
            await ClientForAsync(factory, learnerUser), learnerRecord, learnerUser, await ClientForAsync(factory, otherLearnerUser), otherLearnerUser);
    }

    public static async Task<HttpClient> ClientForAsync(ApiFactory factory, User user)
    {
        var client = factory.CreateApiClient();
        var (login, _) = await Identity.AuthTestSupport.LoginExpectingSessionAsync(client, user.UserName, Identity.AuthTestSupport.DefaultPassword);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Authenticated!.AccessToken);
        return client;
    }

    public static async Task<CourseContentResponse> CreateUnitAsync(HttpClient manager, Guid sectionId, string title = "Week 1")
    {
        var response = await manager.PostAsJsonAsync($"/api/v1/sections/{sectionId}/content", new CreateCourseContentRequest(title, "الأسبوع", null), SisTestSupport.Json);
        return await SisTestSupport.ReadAsync<CourseContentResponse>(response, HttpStatusCode.Created);
    }

    public static async Task<ContentItemResponse> AddItemAsync(HttpClient manager, Guid contentId, ContentItemType type = ContentItemType.Page, string? body = "Read this first.", bool publish = false)
    {
        var response = await manager.PostAsJsonAsync($"/api/v1/content/{contentId}/items",
            new CreateContentItemRequest("Item " + type, "عنصر", type, body, null), SisTestSupport.Json);
        var item = await SisTestSupport.ReadAsync<ContentItemResponse>(response, HttpStatusCode.Created);
        if (!publish)
        {
            return item;
        }

        return await SisTestSupport.ReadAsync<ContentItemResponse>(await manager.PostAsync($"/api/v1/content/{contentId}/items/{item.Id}/publish", null));
    }

    public static Task<HttpResponseMessage> UploadAsync(HttpClient client, Guid contentId, Guid itemId, string fileName, byte[] bytes, string contentType = "application/pdf")
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "file", fileName);
        return client.PostAsync($"/api/v1/content/{contentId}/items/{itemId}/resources", form);
    }

    public static async Task<ResourceResponse> UploadExpectingCreatedAsync(HttpClient client, Guid contentId, Guid itemId, string fileName, byte[] bytes)
    {
        var response = await UploadAsync(client, contentId, itemId, fileName, bytes);
        return await SisTestSupport.ReadAsync<ResourceResponse>(response, HttpStatusCode.Created);
    }

    /// <summary>A published file item with one uploaded resource, built by the manager.</summary>
    public static async Task<(CourseContentResponse Unit, ContentItemResponse Item, ResourceResponse Resource)> PublishedFileAsync(HttpClient manager, Guid sectionId, byte[]? bytes = null)
    {
        var unit = await CreateUnitAsync(manager, sectionId);
        var item = await AddItemAsync(manager, unit.Id, ContentItemType.File, null);
        var resource = await UploadExpectingCreatedAsync(manager, unit.Id, item.Id, "notes.pdf", bytes ?? "%PDF-1.4 demo"u8.ToArray());
        var published = await SisTestSupport.ReadAsync<ContentItemResponse>(await manager.PostAsync($"/api/v1/content/{unit.Id}/items/{item.Id}/publish", null));
        return (unit, published, resource);
    }
}
