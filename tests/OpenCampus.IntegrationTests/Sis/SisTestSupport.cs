using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenCampus.Identity.Application.Authorization;
using OpenCampus.Identity.Domain.Users;
using OpenCampus.IntegrationTests.Identity;
using OpenCampus.Sis.Application.Courses;
using OpenCampus.Sis.Application.Enrolments;
using OpenCampus.Sis.Application.Learners;
using OpenCampus.Sis.Application.Programmes;
using OpenCampus.Sis.Application.Sections;
using OpenCampus.Sis.Domain.Learners;
using OpenCampus.Sis.Domain.Sections;

namespace OpenCampus.IntegrationTests.Sis;

/// <summary>Builds the academic structure through the public interface, exactly as an administrator would (AC-02).</summary>
internal static class SisTestSupport
{
    public static readonly JsonSerializerOptions Json = CreateJson();

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    /// <summary>A client whose every request carries the bearer token of a freshly seeded user in the given role.</summary>
    public static async Task<(HttpClient Client, User User)> ClientAsAsync(ApiFactory factory, string role)
    {
        var client = factory.CreateApiClient();
        var (user, token, _) = await AuthTestSupport.LoginAsRoleAsync(factory, client, role);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (client, user);
    }

    public static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..Math.Min(prefix.Length + 8, 20)].ToUpperInvariant();

    public static async Task<T> ReadAsync<T>(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(expected, body);
        return JsonSerializer.Deserialize<T>(body, Json)!;
    }

    public static async Task<ProgrammeResponse> CreateProgrammeAsync(HttpClient admin)
    {
        var response = await admin.PostAsJsonAsync("/api/v1/programmes",
            new CreateProgrammeRequest(Unique("P"), "Programme " + Guid.NewGuid().ToString("N")[..6], "برنامج", 24), Json);
        return await ReadAsync<ProgrammeResponse>(response, HttpStatusCode.Created);
    }

    public static async Task<CourseResponse> CreateCourseAsync(HttpClient admin, Guid programmeId)
    {
        var response = await admin.PostAsJsonAsync("/api/v1/courses",
            new CreateCourseRequest(programmeId, Unique("C"), "Course", "مقرر", "Description", null, 3), Json);
        return await ReadAsync<CourseResponse>(response, HttpStatusCode.Created);
    }

    public static async Task<SectionDetailResponse> CreateSectionAsync(HttpClient admin, Guid courseId, Guid instructorUserId, int capacity = 2, bool open = true)
    {
        var response = await admin.PostAsJsonAsync("/api/v1/sections",
            new CreateSectionRequest(courseId, Unique("S"), "2026 Autumn", new DateOnly(2026, 9, 7), new DateOnly(2026, 12, 18), capacity, instructorUserId, DeliveryMode.InPerson),
            Json);
        var section = await ReadAsync<SectionDetailResponse>(response, HttpStatusCode.Created);
        if (!open)
        {
            return section;
        }

        return await ReadAsync<SectionDetailResponse>(await admin.PostAsync($"/api/v1/sections/{section.Section.Id}/open", null));
    }

    /// <summary>Seeds a Learner-role account and creates its learner record.</summary>
    public static async Task<(LearnerResponse Learner, User User)> CreateLearnerAsync(ApiFactory factory, HttpClient admin)
    {
        var user = await AuthTestSupport.SeedUserAsync(factory, roles: RoleNames.Learner);
        var response = await admin.PostAsJsonAsync("/api/v1/learners",
            new CreateLearnerRequest(user.Id, Unique("L"), null, new DateOnly(2004, 3, 15), Gender.Female, "+966500000001"), Json);
        return (await ReadAsync<LearnerResponse>(response, HttpStatusCode.Created), user);
    }

    public static Task<HttpResponseMessage> EnrolAsync(HttpClient client, Guid learnerId, Guid sectionId) =>
        client.PostAsJsonAsync("/api/v1/enrolments", new CreateEnrolmentRequest(learnerId, sectionId), Json);

    /// <summary>Asserts the 422 problem of a section-14 rule, whose title carries the rule reference (API-07).</summary>
    public static async Task ShouldBeRuleViolationAsync(this HttpResponseMessage response, string ruleCode)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity, body);
        using var problem = JsonDocument.Parse(body);
        problem.RootElement.GetProperty("title").GetString().ShouldBe(ruleCode);
        problem.RootElement.GetProperty("type").GetString().ShouldBe($"urn:opencampus:error:{ruleCode}");
    }

    public static async Task ShouldBeFieldValidationErrorAsync(this HttpResponseMessage response, string field)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest, body);
        using var problem = JsonDocument.Parse(body);
        problem.RootElement.GetProperty("errors").TryGetProperty(field, out _).ShouldBeTrue(body);
    }
}
