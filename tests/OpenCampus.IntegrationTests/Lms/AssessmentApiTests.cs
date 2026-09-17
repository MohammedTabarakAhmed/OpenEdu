using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using OpenCampus.IntegrationTests.Sis;
using OpenCampus.Lms.Application.Assessment;
using OpenCampus.Lms.Domain.Attendance;
using OpenCampus.Sis.Application.Sections;
using static OpenCampus.IntegrationTests.Lms.LmsTestSupport;

namespace OpenCampus.IntegrationTests.Lms;

/// <summary>
/// The Increment 5 API surface (15.3 "Assessment and grading"): the full assignment cycle from publication
/// through submission to marking, and attendance recording. Scope per section (SEC-12) is asserted with the
/// non-member and other-instructor refusals returning 404 (API-06).
/// </summary>
[Collection(ApiCollection.Name)]
public class AssessmentApiTests(ApiFactory factory)
{
    [Fact]
    public async Task FullAssignmentCycle_PublishSubmitMark()
    {
        var cast = await BuildSectionCastAsync(factory);

        // Draft: invisible to the learner (BR: published-only listing).
        var created = await SisTestSupport.ReadAsync<AssignmentResponse>(
            await cast.Instructor.PostAsJsonAsync($"/api/v1/sections/{cast.SectionId}/assignments",
                new AssignmentRequest("Essay 1", "مقال 1", "Write 500 words.", 100m, DateTime.UtcNow.AddDays(7), false, 0m), SisTestSupport.Json),
            HttpStatusCode.Created);
        created.IsPublished.ShouldBeFalse();

        var learnerListBeforePublish = await SisTestSupport.ReadAsync<SectionAssignmentsResponse>(
            await cast.Learner.GetAsync($"/api/v1/sections/{cast.SectionId}/assignments"));
        learnerListBeforePublish.Assignments.ShouldBeEmpty();
        (await cast.Learner.GetAsync($"/api/v1/assignments/{created.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var published = await SisTestSupport.ReadAsync<AssignmentResponse>(
            await cast.Instructor.PostAsync($"/api/v1/assignments/{created.Id}/publish", null));
        published.IsPublished.ShouldBeTrue();

        // The learner submits text and a file; a non-enrolled learner is refused as 404 (SEC-12).
        var form = new MultipartFormDataContent { { new StringContent("My answer."), "textBody" } };
        var file = new ByteArrayContent("%PDF-1.4 essay"u8.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", "essay.pdf");
        var submission = await SisTestSupport.ReadAsync<SubmissionResponse>(
            await cast.Learner.PostAsync($"/api/v1/assignments/{created.Id}/submit", form));
        submission.TextBody.ShouldBe("My answer.");
        submission.HasFile.ShouldBeTrue();
        submission.Score.ShouldBeNull();

        var otherForm = new MultipartFormDataContent { { new StringContent("Not enrolled."), "textBody" } };
        (await cast.OtherLearner.PostAsync($"/api/v1/assignments/{created.Id}/submit", otherForm)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // The submitted file downloads for its owner but not for an unrelated learner.
        var ownDownload = await cast.Learner.GetAsync($"/api/v1/submissions/{submission.Id}/file");
        ownDownload.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ownDownload.Content.ReadAsByteArrayAsync()).ShouldBe("%PDF-1.4 essay"u8.ToArray());
        (await cast.OtherLearner.GetAsync($"/api/v1/submissions/{submission.Id}/file")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // The manager sees the submission (and the still-missing other enrolled learners), then marks it.
        var submissions = await SisTestSupport.ReadAsync<AssignmentSubmissionsResponse>(
            await cast.Instructor.GetAsync($"/api/v1/assignments/{created.Id}/submissions"));
        submissions.Submissions.ShouldContain(s => s.Id == submission.Id);

        var marked = await SisTestSupport.ReadAsync<SubmissionResponse>(
            await cast.Instructor.PostAsJsonAsync($"/api/v1/submissions/{submission.Id}/mark", new MarkSubmissionRequest(88m, "Good work."), SisTestSupport.Json));
        marked.Score.ShouldBe(88m);
        marked.Feedback.ShouldBe("Good work.");

        // Another instructor, not assigned to this section, cannot manage it (SEC-12).
        (await cast.OtherInstructor.GetAsync($"/api/v1/assignments/{created.Id}/submissions")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Attendance_RecordedAgainstEnrolledLearners_UnknownLearnerRefused()
    {
        var cast = await BuildSectionCastAsync(factory);
        var session = await SisTestSupport.ReadAsync<SessionResponse>(
            await cast.Admin.PostAsJsonAsync($"/api/v1/sections/{cast.SectionId}/sessions",
                new SessionRequest(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(-1).AddHours(2), "Room 1"), SisTestSupport.Json),
            HttpStatusCode.Created);

        var register = await SisTestSupport.ReadAsync<SessionRegisterResponse>(
            await cast.Instructor.PostAsJsonAsync($"/api/v1/sections/{cast.SectionId}/sessions/{session.Id}/attendance",
                new RecordAttendanceRequest([new AttendanceEntry(cast.LearnerUser.Id, AttendanceStatus.Present)]), SisTestSupport.Json));
        register.Records.ShouldContain(r => r.LearnerUserId == cast.LearnerUser.Id && r.Status == AttendanceStatus.Present);

        var unknown = await cast.Instructor.PostAsJsonAsync($"/api/v1/sections/{cast.SectionId}/sessions/{session.Id}/attendance",
            new RecordAttendanceRequest([new AttendanceEntry(cast.OtherLearnerUser.Id, AttendanceStatus.Present)]), SisTestSupport.Json);
        await unknown.ShouldBeFieldValidationErrorAsync("Entries");

        var sectionView = await SisTestSupport.ReadAsync<SectionAttendanceResponse>(await cast.Instructor.GetAsync($"/api/v1/sections/{cast.SectionId}/attendance"));
        sectionView.Learners.Single(l => l.LearnerUserId == cast.LearnerUser.Id).AttendancePercent.ShouldBe(100m);

        // The learner sees only their own record; a learner who was never enrolled cannot read the section at all.
        var learnerView = await SisTestSupport.ReadAsync<SectionAttendanceResponse>(await cast.Learner.GetAsync($"/api/v1/sections/{cast.SectionId}/attendance"));
        learnerView.CanManage.ShouldBeFalse();
        learnerView.Records.ShouldContain(r => r.LearnerUserId == cast.LearnerUser.Id);
    }
}
