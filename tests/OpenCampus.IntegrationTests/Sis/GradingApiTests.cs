using System.Net;
using System.Net.Http.Json;
using OpenCampus.Identity.Application.Authorization;
using OpenCampus.IntegrationTests.Lms;
using OpenCampus.Sis.Application.Enrolments;
using OpenCampus.Sis.Application.Grading;
using OpenCampus.Sis.Domain.Enrolments;

namespace OpenCampus.IntegrationTests.Sis;

/// <summary>
/// The Increment 5 grading API (15.3): record, release, and the learner's own released results (BR-06). Grades
/// are unavailable to the learner before release; SEC-12 scope refuses another instructor as 404.
/// </summary>
[Collection(ApiCollection.Name)]
public class GradingApiTests(ApiFactory factory)
{
    [Fact]
    public async Task RecordAmendRelease_LearnerSeesNothingUntilReleased()
    {
        var (admin, _) = await SisTestSupport.ClientAsAsync(factory, RoleNames.Administrator);
        var (instructor, instructorUser) = await SisTestSupport.ClientAsAsync(factory, RoleNames.Instructor);
        var (otherInstructor, _) = await SisTestSupport.ClientAsAsync(factory, RoleNames.Instructor);

        var programme = await SisTestSupport.CreateProgrammeAsync(admin);
        var course = await SisTestSupport.CreateCourseAsync(admin, programme.Id);
        var section = await SisTestSupport.CreateSectionAsync(admin, course.Id, instructorUser.Id, capacity: 5);
        var component = section.GradeComponents.Single();

        var (learnerRecord, learnerUser) = await SisTestSupport.CreateLearnerAsync(factory, admin);
        var enrolResponse = await SisTestSupport.EnrolAsync(admin, learnerRecord.Id, section.Section.Id);
        var enrolment = await SisTestSupport.ReadAsync<EnrolmentResponse>(enrolResponse, HttpStatusCode.Created);
        var learnerClient = await LmsTestSupport.ClientForAsync(factory, learnerUser);

        // Before release: nothing recorded is visible to the learner.
        var before = await SisTestSupport.ReadAsync<LearnerResultsResponse>(await learnerClient.GetAsync($"/api/v1/me/enrolments/{enrolment.Id}/results"));
        before.IsReleased.ShouldBeFalse();
        before.Entries.ShouldBeEmpty();
        before.FinalGrade.ShouldBeNull();

        // Another instructor, not assigned to the section, cannot record grades (SEC-12).
        (await otherInstructor.PostAsJsonAsync($"/api/v1/sections/{section.Section.Id}/grades",
            new RecordGradeRequest(enrolment.Id, component.Id, 70m), SisTestSupport.Json)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Record, then amend.
        var recorded = await SisTestSupport.ReadAsync<GradeEntryResponse>(
            await instructor.PostAsJsonAsync($"/api/v1/sections/{section.Section.Id}/grades", new RecordGradeRequest(enrolment.Id, component.Id, 70m), SisTestSupport.Json));
        recorded.Score.ShouldBe(70m);
        recorded.IsReleased.ShouldBeFalse();

        var amended = await SisTestSupport.ReadAsync<GradeEntryResponse>(
            await instructor.PostAsJsonAsync($"/api/v1/sections/{section.Section.Id}/grades", new RecordGradeRequest(enrolment.Id, component.Id, 82m), SisTestSupport.Json));
        amended.Id.ShouldBe(recorded.Id);
        amended.Score.ShouldBe(82m);

        // Still unreleased: the learner still sees nothing (BR-06).
        var stillBefore = await SisTestSupport.ReadAsync<LearnerResultsResponse>(await learnerClient.GetAsync($"/api/v1/me/enrolments/{enrolment.Id}/results"));
        stillBefore.Entries.ShouldBeEmpty();

        var gradebook = await SisTestSupport.ReadAsync<GradebookResponse>(await instructor.GetAsync($"/api/v1/sections/{section.Section.Id}/grades"));
        gradebook.UngradedPairCount.ShouldBe(0);
        gradebook.IsReleased.ShouldBeFalse();

        var released = await SisTestSupport.ReadAsync<GradebookResponse>(await instructor.PostAsync($"/api/v1/sections/{section.Section.Id}/grades/release", null));
        released.IsReleased.ShouldBeTrue();

        // After release: the learner's own released entry and final grade (BR-06); the enrolment is completed.
        var after = await SisTestSupport.ReadAsync<LearnerResultsResponse>(await learnerClient.GetAsync($"/api/v1/me/enrolments/{enrolment.Id}/results"));
        after.IsReleased.ShouldBeTrue();
        after.Entries.Single().Score.ShouldBe(82m);
        after.Status.ShouldBe(EnrolmentStatus.Completed);
        after.FinalGrade.ShouldBe(82m);
    }
}
