using System.Net;
using System.Net.Http.Json;
using OpenCampus.Identity.Application.Authorization;
using OpenCampus.IntegrationTests.Lms;
using OpenCampus.Sis.Application.Grading;
using OpenCampus.Sis.Application.Reports;

namespace OpenCampus.IntegrationTests.Sis;

/// <summary>
/// Operational reporting (BO-04) through the interface: participation counts per section by enrolment standing,
/// attainment after release against the configured pass threshold, filtering by programme/course/term, and the
/// `sis.report.read` gate (Administrator and Registrar; not Instructor or Learner).
/// </summary>
[Collection(ApiCollection.Name)]
public class ReportsApiTests(ApiFactory factory)
{
    [Fact]
    public async Task Participation_ThenAttainment_ReflectTheSectionAsItProgresses()
    {
        var cast = await LmsTestSupport.BuildSectionCastAsync(factory);
        var section = await SisTestSupport.ReadAsync<OpenCampus.Sis.Application.Sections.SectionDetailResponse>(await cast.Admin.GetAsync($"/api/v1/sections/{cast.SectionId}"));
        var (other, _) = await SisTestSupport.CreateLearnerAsync(factory, cast.Admin);
        (await SisTestSupport.EnrolAsync(cast.Admin, other.Id, cast.SectionId)).StatusCode.ShouldBe(HttpStatusCode.Created);
        var enrolments = await SisTestSupport.ReadAsync<OpenCampus.SharedKernel.PagedResponse<OpenCampus.Sis.Application.Enrolments.EnrolmentResponse>>(
            await cast.Admin.GetAsync($"/api/v1/sections/{cast.SectionId}/enrolments"));
        var otherEnrolment = enrolments.Items.Single(e => e.Learner.LearnerId == other.Id);
        (await cast.Admin.DeleteAsync($"/api/v1/enrolments/{otherEnrolment.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // Participation, narrowed to this course: one active, one withdrawn.
        var participation = await SisTestSupport.ReadAsync<ParticipationReport>(await cast.Admin.GetAsync($"/api/v1/reports/participation?courseId={section.Section.CourseId}"));
        var row = participation.Rows.Single(r => r.SectionId == cast.SectionId);
        row.Active.ShouldBe(1);
        row.Withdrawn.ShouldBe(1);
        row.Completed.ShouldBe(0);
        row.Capacity.ShouldBe(10);
        row.OccupancyPercent.ShouldBe(10m);
        participation.Sections.ShouldBe(participation.Rows.Count);
        participation.Withdrawn.ShouldBeGreaterThanOrEqualTo(1);

        // Filters: a term that does not exist yields no rows; the programme filter includes the section.
        (await SisTestSupport.ReadAsync<ParticipationReport>(await cast.Admin.GetAsync("/api/v1/reports/participation?term=NoSuchTerm"))).Rows.ShouldBeEmpty();
        (await SisTestSupport.ReadAsync<ParticipationReport>(await cast.Admin.GetAsync($"/api/v1/reports/participation?programmeId={row.ProgrammeId}"))).Rows.ShouldContain(r => r.SectionId == cast.SectionId);

        // Attainment: nothing before release; after release the completed learner counts against the threshold (50).
        (await SisTestSupport.ReadAsync<AttainmentReport>(await cast.Admin.GetAsync($"/api/v1/reports/attainment?courseId={section.Section.CourseId}"))).Rows.ShouldBeEmpty();

        var activeEnrolment = enrolments.Items.Single(e => e.Learner.LearnerId == cast.LearnerRecord.Id);
        (await cast.Instructor.PostAsJsonAsync($"/api/v1/sections/{cast.SectionId}/grades", new RecordGradeRequest(activeEnrolment.Id, section.GradeComponents.Single().Id, 40m), SisTestSupport.Json)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await cast.Instructor.PostAsync($"/api/v1/sections/{cast.SectionId}/grades/release", null)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var attainment = await SisTestSupport.ReadAsync<AttainmentReport>(await cast.Admin.GetAsync($"/api/v1/reports/attainment?courseId={section.Section.CourseId}"));
        attainment.PassThresholdPercent.ShouldBe(50m);
        var outcome = attainment.Rows.Single(r => r.SectionId == cast.SectionId);
        outcome.SectionId.ShouldBe(cast.SectionId);
        outcome.Completed.ShouldBe(1);
        outcome.Passed.ShouldBe(0);
        outcome.PassRatePercent.ShouldBe(0m);
        outcome.AverageFinalGrade.ShouldBe(40m);
        outcome.CertificatesIssued.ShouldBe(0);

        var after = await SisTestSupport.ReadAsync<ParticipationReport>(await cast.Admin.GetAsync($"/api/v1/reports/participation?courseId={section.Section.CourseId}"));
        after.Rows.Single(r => r.SectionId == cast.SectionId).Completed.ShouldBe(1);
        after.Rows.Single(r => r.SectionId == cast.SectionId).Active.ShouldBe(0);
    }

    [Fact]
    public async Task Reports_RequireReportRead()
    {
        var cast = await LmsTestSupport.BuildSectionCastAsync(factory);
        var (registrar, _) = await SisTestSupport.ClientAsAsync(factory, RoleNames.Registrar);
        using var anonymous = factory.CreateClient();

        (await anonymous.GetAsync("/api/v1/reports/participation")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await cast.Instructor.GetAsync("/api/v1/reports/participation")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await cast.Learner.GetAsync("/api/v1/reports/attainment")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await registrar.GetAsync("/api/v1/reports/participation")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await registrar.GetAsync("/api/v1/reports/attainment")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await cast.Admin.GetAsync("/api/v1/reports/participation")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
