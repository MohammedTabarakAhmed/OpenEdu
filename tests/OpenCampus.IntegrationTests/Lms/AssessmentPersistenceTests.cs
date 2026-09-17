using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenCampus.IntegrationTests.Sis;
using OpenCampus.Lms.Application.Abstractions;
using OpenCampus.Lms.Domain.Assessment;
using OpenCampus.Lms.Domain.Attendance;
using OpenCampus.Sis.Application.Abstractions;
using OpenCampus.Sis.Application.Grading;
using OpenCampus.Sis.Application.Sections;
using OpenCampus.Sis.Domain.Enrolments;
using static OpenCampus.IntegrationTests.Lms.LmsTestSupport;

namespace OpenCampus.IntegrationTests.Lms;

/// <summary>Increment 5 infrastructure: assignment/attendance/grade-entry persistence, the extended LMS→SIS adapter and BR-12 through the outcomes contract.</summary>
[Collection(ApiCollection.Name)]
public class AssessmentPersistenceTests(ApiFactory factory)
{
    private static readonly DateTime Due = new(2026, 10, 15, 23, 59, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Assignment_RoundTripsWithSubmissions_AndOneRowPerLearner()
    {
        var sectionId = Guid.NewGuid();
        var learnerA = Guid.NewGuid();
        var learnerB = Guid.NewGuid();
        var assignment = Assignment.Create(sectionId, "Essay", "مقال", "Write.", 100m, Due, true, 10m);
        assignment.Publish();
        assignment.Submit(learnerA, true, "v1", null, Due.AddDays(-1));
        assignment.Submit(learnerA, true, "v2", null, Due.AddHours(-1)); // replaces
        var late = assignment.Submit(learnerB, true, "late", "submissions/x/y.pdf", Due.AddHours(2));
        assignment.Mark(late.Id, 80m, "ok", Guid.NewGuid(), Due.AddDays(2));

        using (var scope = factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<IAssignmentRepository>().Add(assignment);
            await scope.ServiceProvider.GetRequiredService<ILmsUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        using (var scope = factory.Services.CreateScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IAssignmentRepository>();
            var loaded = await repository.FindByIdAsync(assignment.Id, CancellationToken.None);
            loaded.ShouldNotBeNull();
            loaded.Submissions.Count.ShouldBe(2);
            loaded.FindSubmissionByLearner(learnerA)!.TextBody.ShouldBe("v2");
            loaded.FindSubmissionByLearner(learnerB)!.Score.ShouldBe(72m); // 80 × 0.9 late penalty

            (await repository.FindBySubmissionIdAsync(late.Id, CancellationToken.None))!.Id.ShouldBe(assignment.Id);
            (await repository.ListBySectionAsync(sectionId, CancellationToken.None)).ShouldHaveSingleItem();
            (await repository.ListPublishedBySectionsAsync([sectionId, Guid.NewGuid()], CancellationToken.None)).ShouldHaveSingleItem();
        }
    }

    [Fact]
    public async Task Attendance_IsUniquePerSessionAndLearner_AndListsBySessions()
    {
        var sessionA = Guid.NewGuid();
        var sessionB = Guid.NewGuid();
        var learner = Guid.NewGuid();
        var now = DateTime.UtcNow;

        using (var scope = factory.Services.CreateScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IAttendanceRepository>();
            repository.Add(AttendanceRecord.Create(sessionA, now.AddHours(-2), learner, AttendanceStatus.Present, Guid.NewGuid(), now));
            repository.Add(AttendanceRecord.Create(sessionB, now.AddHours(-1), learner, AttendanceStatus.Absent, Guid.NewGuid(), now));
            await scope.ServiceProvider.GetRequiredService<ILmsUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        using (var scope = factory.Services.CreateScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IAttendanceRepository>();
            (await repository.FindAsync(sessionA, learner, CancellationToken.None))!.Status.ShouldBe(AttendanceStatus.Present);
            (await repository.ListBySessionsAsync([sessionA, sessionB], CancellationToken.None)).Count.ShouldBe(2);
            (await repository.ListBySessionAsync(sessionB, CancellationToken.None)).ShouldHaveSingleItem();

            repository.Add(AttendanceRecord.Create(sessionA, now.AddHours(-2), learner, AttendanceStatus.Late, Guid.NewGuid(), now));
            await Should.ThrowAsync<DbUpdateException>(() => scope.ServiceProvider.GetRequiredService<ILmsUnitOfWork>().SaveChangesAsync(CancellationToken.None));
        }
    }

    [Fact]
    public async Task GradeEntries_ListBySection_AndUniquePerEnrolmentAndComponent()
    {
        var cast = await BuildSectionCastAsync(factory);
        var section = await SisTestSupport.ReadAsync<SectionDetailResponse>(await cast.Admin.GetAsync($"/api/v1/sections/{cast.SectionId}"));
        var componentId = section.GradeComponents.First().Id;

        using (var scope = factory.Services.CreateScope())
        {
            var enrolments = scope.ServiceProvider.GetRequiredService<IEnrolmentRepository>();
            var sections = scope.ServiceProvider.GetRequiredService<ISectionRepository>();
            var grades = scope.ServiceProvider.GetRequiredService<IGradeEntryRepository>();

            var enrolment = (await enrolments.ListActiveInSectionAsync(cast.SectionId, CancellationToken.None)).ShouldHaveSingleItem();
            var aggregate = (await sections.FindByIdAsync(cast.SectionId, CancellationToken.None))!;
            grades.Add(GradeEntry.Create(enrolment, aggregate.GradeComponents.Single(c => c.Id == componentId), 55m, cast.InstructorUser.Id, DateTime.UtcNow));
            await scope.ServiceProvider.GetRequiredService<ISisUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        using (var scope = factory.Services.CreateScope())
        {
            var grades = scope.ServiceProvider.GetRequiredService<IGradeEntryRepository>();
            var bySection = await grades.ListBySectionAsync(cast.SectionId, CancellationToken.None);
            bySection.ShouldHaveSingleItem().Score.ShouldBe(55m);
            var enrolmentId = bySection[0].EnrolmentId;
            (await grades.FindByEnrolmentAndComponentAsync(enrolmentId, componentId, CancellationToken.None)).ShouldNotBeNull();
            (await grades.ListByEnrolmentAsync(enrolmentId, CancellationToken.None)).ShouldHaveSingleItem();

            var enrolments = scope.ServiceProvider.GetRequiredService<IEnrolmentRepository>();
            var enrolment = (await enrolments.FindByUserAndSectionAsync(cast.LearnerUser.Id, cast.SectionId, CancellationToken.None))!;
            enrolment.Id.ShouldBe(enrolmentId);
            (await enrolments.FindByUserAndSectionAsync(Guid.NewGuid(), cast.SectionId, CancellationToken.None)).ShouldBeNull();

            var sections = scope.ServiceProvider.GetRequiredService<ISectionRepository>();
            var aggregate = (await sections.FindByIdAsync(cast.SectionId, CancellationToken.None))!;
            grades.Add(GradeEntry.Create(enrolment, aggregate.GradeComponents.Single(c => c.Id == componentId), 60m, cast.InstructorUser.Id, DateTime.UtcNow));
            await Should.ThrowAsync<DbUpdateException>(() => scope.ServiceProvider.GetRequiredService<ISisUnitOfWork>().SaveChangesAsync(CancellationToken.None));
        }
    }

    [Fact]
    public async Task Adapter_ListsSessionsAndEnrolledLearners_ThroughTheContract()
    {
        var cast = await BuildSectionCastAsync(factory);
        var start = new DateTime(2026, 9, 14, 9, 0, 0, DateTimeKind.Utc);
        (await cast.Admin.PostAsJsonAsync($"/api/v1/sections/{cast.SectionId}/sessions", new SessionRequest(start, start.AddHours(2), "Room 1"), SisTestSupport.Json))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        using var scope = factory.Services.CreateScope();
        var access = scope.ServiceProvider.GetRequiredService<ISectionAccess>();

        var sessions = await access.ListSessionsAsync(cast.SectionId, CancellationToken.None);
        sessions.ShouldHaveSingleItem().ScheduledStartUtc.ShouldBe(start);

        var learners = await access.ListEnrolledLearnersAsync(cast.SectionId, CancellationToken.None);
        learners.ShouldHaveSingleItem().UserId.ShouldBe(cast.LearnerUser.Id);
        learners[0].LearnerNumber.ShouldBe(cast.LearnerRecord.LearnerNumber);
        (await access.ListEnrolledLearnersAsync(Guid.NewGuid(), CancellationToken.None)).ShouldBeEmpty();
        (await access.ListSessionsAsync(Guid.NewGuid(), CancellationToken.None)).ShouldBeEmpty();
    }

    [Fact]
    public async Task BR12_ReportedAttendanceRate_PlacesEnrolmentAtRisk_AndRecoveryRestoresIt()
    {
        var cast = await BuildSectionCastAsync(factory);

        using var scope = factory.Services.CreateScope();
        var outcomes = scope.ServiceProvider.GetRequiredService<IAssessmentOutcomes>();
        var threshold = scope.ServiceProvider.GetRequiredService<IOptions<AcademicOptions>>().Value.AttendanceThresholdPercent;
        var enrolments = scope.ServiceProvider.GetRequiredService<IEnrolmentRepository>();

        await outcomes.ReportAttendanceRateAsync(cast.SectionId, cast.LearnerUser.Id, threshold - 1, CancellationToken.None);
        (await enrolments.FindByUserAndSectionAsync(cast.LearnerUser.Id, cast.SectionId, CancellationToken.None))!.Status.ShouldBe(EnrolmentStatus.AtRisk);

        await outcomes.ReportAttendanceRateAsync(cast.SectionId, cast.LearnerUser.Id, threshold, CancellationToken.None);
        (await enrolments.FindByUserAndSectionAsync(cast.LearnerUser.Id, cast.SectionId, CancellationToken.None))!.Status.ShouldBe(EnrolmentStatus.Active);

        // Unknown learner or section: nothing to apply, nothing thrown.
        await Should.NotThrowAsync(() => outcomes.ReportAttendanceRateAsync(cast.SectionId, Guid.NewGuid(), 0m, CancellationToken.None));
    }
}
