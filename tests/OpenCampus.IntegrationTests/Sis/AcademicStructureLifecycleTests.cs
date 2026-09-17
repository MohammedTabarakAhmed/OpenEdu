using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using OpenCampus.Identity.Application.Authorization;
using OpenCampus.IntegrationTests.Identity;
using OpenCampus.SharedKernel;
using OpenCampus.Sis.Application.Courses;
using OpenCampus.Sis.Application.Enrolments;
using OpenCampus.Sis.Application.Learners;
using OpenCampus.Sis.Application.Programmes;
using OpenCampus.Sis.Application.Sections;
using OpenCampus.Sis.Domain.Enrolments;
using OpenCampus.Sis.Domain.Learners;
using OpenCampus.Sis.Domain.Sections;
using static OpenCampus.IntegrationTests.Sis.SisTestSupport;

namespace OpenCampus.IntegrationTests.Sis;

/// <summary>
/// Increment 3 exit criteria: the catalogue lifecycle demonstrated end to end (programme → course → section →
/// sessions and grade scheme → open → enrolled learners, AC-02) and the capacity and duplicate-enrolment
/// rejections demonstrated against the database (BR-01, BR-02; also BR-03, BR-14, BR-16).
/// </summary>
[Collection(ApiCollection.Name)]
public class AcademicStructureLifecycleTests(ApiFactory factory)
{
    [Fact]
    public async Task Administrator_CompletesLifecycle_FromProgrammeToOpenSectionWithEnrolledLearners()
    {
        var (admin, _) = await ClientAsAsync(factory, RoleNames.Administrator);
        var instructor = await AuthTestSupport.SeedUserAsync(factory, roles: RoleNames.Instructor);

        // Programme: create (201 + Location), retrieve, amend.
        var created = await admin.PostAsJsonAsync("/api/v1/programmes", new CreateProgrammeRequest("bsc-test", "Test Programme", "برنامج اختباري", 36), Json);
        var programme = await ReadAsync<ProgrammeResponse>(created, HttpStatusCode.Created);
        created.Headers.Location.ShouldNotBeNull();
        created.Headers.Location!.ToString().ShouldContain(programme.Id.ToString());
        programme.Code.ShouldBe("BSC-TEST");

        var fetched = await ReadAsync<ProgrammeResponse>(await admin.GetAsync(created.Headers.Location));
        fetched.Id.ShouldBe(programme.Id);

        var amended = await ReadAsync<ProgrammeResponse>(await admin.PutAsJsonAsync($"/api/v1/programmes/{programme.Id}",
            new UpdateProgrammeRequest("Test Programme (amended)", "برنامج اختباري", 48, true), Json));
        amended.DurationMonths.ShouldBe(48);
        amended.ModifiedAtUtc.ShouldNotBeNull();

        // Course under the programme; the programme can no longer be deleted (409).
        var course = await CreateCourseAsync(admin, programme.Id);
        course.ProgrammeCode.ShouldBe("BSC-TEST");
        (await admin.DeleteAsync($"/api/v1/programmes/{programme.Id}")).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        // Section in Draft with an Instructor-role account; sessions and a grade scheme; then Open.
        var draft = await CreateSectionAsync(admin, course.Id, instructor.Id, capacity: 2, open: false);
        draft.Section.Status.ShouldBe(SectionStatus.Draft);
        draft.Section.Instructor!.UserId.ShouldBe(instructor.Id);
        var sectionId = draft.Section.Id;

        var start = new DateTime(2026, 9, 8, 9, 0, 0, DateTimeKind.Utc);
        var session = await ReadAsync<SessionResponse>(
            await admin.PostAsJsonAsync($"/api/v1/sections/{sectionId}/sessions", new SessionRequest(start, start.AddHours(2), "Room 1"), Json),
            HttpStatusCode.Created);
        session.Location.ShouldBe("Room 1");

        await ReadAsync<GradeComponentResponse>(
            await admin.PostAsJsonAsync($"/api/v1/sections/{sectionId}/grade-components", new GradeComponentRequest("Coursework", "أعمال", 60m, 100m), Json),
            HttpStatusCode.Created);
        await ReadAsync<GradeComponentResponse>(
            await admin.PostAsJsonAsync($"/api/v1/sections/{sectionId}/grade-components", new GradeComponentRequest("Final", "نهائي", 40m, 100m), Json),
            HttpStatusCode.Created);

        var opened = await ReadAsync<SectionDetailResponse>(await admin.PostAsync($"/api/v1/sections/{sectionId}/open", null));
        opened.Section.Status.ShouldBe(SectionStatus.Open);
        opened.Sessions.Count.ShouldBe(1);
        opened.GradeComponents.Count.ShouldBe(2);
        opened.TotalWeightPercent.ShouldBe(100m);

        // Learners and enrolments up to capacity.
        var (learnerA, _) = await CreateLearnerAsync(factory, admin);
        var (learnerB, _) = await CreateLearnerAsync(factory, admin);

        var enrolmentA = await ReadAsync<EnrolmentResponse>(await EnrolAsync(admin, learnerA.Id, sectionId), HttpStatusCode.Created);
        enrolmentA.Status.ShouldBe(EnrolmentStatus.Active);
        enrolmentA.Learner.LearnerNumber.ShouldBe(learnerA.LearnerNumber);
        enrolmentA.Section.CourseCode.ShouldBe(course.Code);
        await ReadAsync<EnrolmentResponse>(await EnrolAsync(admin, learnerB.Id, sectionId), HttpStatusCode.Created);

        var detail = await ReadAsync<SectionDetailResponse>(await admin.GetAsync($"/api/v1/sections/{sectionId}"));
        detail.Section.ActiveEnrolmentCount.ShouldBe(2);

        var bySection = await ReadAsync<PagedResponse<EnrolmentResponse>>(await admin.GetAsync($"/api/v1/sections/{sectionId}/enrolments"));
        bySection.TotalCount.ShouldBe(2);
        var byLearner = await ReadAsync<PagedResponse<EnrolmentResponse>>(await admin.GetAsync($"/api/v1/learners/{learnerA.Id}/enrolments"));
        byLearner.Items.ShouldHaveSingleItem().Section.SectionId.ShouldBe(sectionId);

        var transcript = await ReadAsync<TranscriptResponse>(await admin.GetAsync($"/api/v1/learners/{learnerA.Id}/transcript"));
        transcript.LearnerNumber.ShouldBe(learnerA.LearnerNumber);
        transcript.Enrolments.Count.ShouldBe(1);
        transcript.CompletedCount.ShouldBe(0);
    }

    // ----- Rule rejections against the database (exit criteria) -----

    [Fact]
    public async Task BR01_EnrolmentBeyondCapacity_IsRejectedWith422()
    {
        var (admin, _) = await ClientAsAsync(factory, RoleNames.Administrator);
        var instructor = await AuthTestSupport.SeedUserAsync(factory, roles: RoleNames.Instructor);
        var course = await CreateCourseAsync(admin, (await CreateProgrammeAsync(admin)).Id);
        var section = await CreateSectionAsync(admin, course.Id, instructor.Id, capacity: 1);

        var (first, _) = await CreateLearnerAsync(factory, admin);
        var (second, _) = await CreateLearnerAsync(factory, admin);
        (await EnrolAsync(admin, first.Id, section.Section.Id)).StatusCode.ShouldBe(HttpStatusCode.Created);

        await (await EnrolAsync(admin, second.Id, section.Section.Id)).ShouldBeRuleViolationAsync("BR-01");

        // Withdrawal frees the place; a further enrolment succeeds.
        var enrolments = await ReadAsync<PagedResponse<EnrolmentResponse>>(await admin.GetAsync($"/api/v1/sections/{section.Section.Id}/enrolments"));
        (await admin.DeleteAsync($"/api/v1/enrolments/{enrolments.Items.Single().Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await EnrolAsync(admin, second.Id, section.Section.Id)).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task BR02_DuplicateActiveEnrolment_IsRejectedWith422_AndReenrolmentAfterWithdrawalReinstates()
    {
        var (admin, _) = await ClientAsAsync(factory, RoleNames.Administrator);
        var instructor = await AuthTestSupport.SeedUserAsync(factory, roles: RoleNames.Instructor);
        var course = await CreateCourseAsync(admin, (await CreateProgrammeAsync(admin)).Id);
        var section = await CreateSectionAsync(admin, course.Id, instructor.Id, capacity: 10);
        var (learner, _) = await CreateLearnerAsync(factory, admin);

        var enrolment = await ReadAsync<EnrolmentResponse>(await EnrolAsync(admin, learner.Id, section.Section.Id), HttpStatusCode.Created);

        await (await EnrolAsync(admin, learner.Id, section.Section.Id)).ShouldBeRuleViolationAsync("BR-02");

        (await admin.DeleteAsync($"/api/v1/enrolments/{enrolment.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var withdrawn = await ReadAsync<EnrolmentResponse>(await admin.GetAsync($"/api/v1/enrolments/{enrolment.Id}"));
        withdrawn.Status.ShouldBe(EnrolmentStatus.Withdrawn);

        // 13.3 unique (LearnerId, SectionId): the same row is reinstated rather than a second one inserted.
        var reinstated = await ReadAsync<EnrolmentResponse>(await EnrolAsync(admin, learner.Id, section.Section.Id), HttpStatusCode.Created);
        reinstated.Id.ShouldBe(enrolment.Id);
        reinstated.Status.ShouldBe(EnrolmentStatus.Active);
    }

    [Fact]
    public async Task BR03_EnrolmentInSectionThatIsNotOpen_IsRejectedWith422()
    {
        var (admin, _) = await ClientAsAsync(factory, RoleNames.Administrator);
        var instructor = await AuthTestSupport.SeedUserAsync(factory, roles: RoleNames.Instructor);
        var course = await CreateCourseAsync(admin, (await CreateProgrammeAsync(admin)).Id);
        var draft = await CreateSectionAsync(admin, course.Id, instructor.Id, open: false);
        var (learner, _) = await CreateLearnerAsync(factory, admin);

        await (await EnrolAsync(admin, learner.Id, draft.Section.Id)).ShouldBeRuleViolationAsync("BR-03");

        await OpenSectionAsync(admin, draft.Section.Id);
        await ReadAsync<SectionDetailResponse>(await admin.PostAsync($"/api/v1/sections/{draft.Section.Id}/close", null));

        await (await EnrolAsync(admin, learner.Id, draft.Section.Id)).ShouldBeRuleViolationAsync("BR-03");
    }

    [Fact]
    public async Task BR14_DeletingSectionWithEnrolments_IsRejectedWith422_AndEmptySectionIsDeleted()
    {
        var (admin, _) = await ClientAsAsync(factory, RoleNames.Administrator);
        var instructor = await AuthTestSupport.SeedUserAsync(factory, roles: RoleNames.Instructor);
        var course = await CreateCourseAsync(admin, (await CreateProgrammeAsync(admin)).Id);
        var enrolled = await CreateSectionAsync(admin, course.Id, instructor.Id);
        var empty = await CreateSectionAsync(admin, course.Id, instructor.Id);
        var (learner, _) = await CreateLearnerAsync(factory, admin);
        var enrolment = await ReadAsync<EnrolmentResponse>(await EnrolAsync(admin, learner.Id, enrolled.Section.Id), HttpStatusCode.Created);

        await (await admin.DeleteAsync($"/api/v1/sections/{enrolled.Section.Id}")).ShouldBeRuleViolationAsync("BR-14");

        // A withdrawn enrolment still counts: "any enrolment exists against it".
        (await admin.DeleteAsync($"/api/v1/enrolments/{enrolment.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await (await admin.DeleteAsync($"/api/v1/sections/{enrolled.Section.Id}")).ShouldBeRuleViolationAsync("BR-14");

        (await admin.DeleteAsync($"/api/v1/sections/{empty.Section.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.GetAsync($"/api/v1/sections/{empty.Section.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await admin.DeleteAsync($"/api/v1/courses/{course.Id}")).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task BR16_OverlappingSessionForSameInstructorAcrossSections_IsRejectedWith422()
    {
        var (admin, _) = await ClientAsAsync(factory, RoleNames.Administrator);
        var instructor = await AuthTestSupport.SeedUserAsync(factory, roles: RoleNames.Instructor);
        var course = await CreateCourseAsync(admin, (await CreateProgrammeAsync(admin)).Id);
        var first = await CreateSectionAsync(admin, course.Id, instructor.Id, open: false);
        var second = await CreateSectionAsync(admin, course.Id, instructor.Id, open: false);
        var start = new DateTime(2026, 10, 5, 13, 0, 0, DateTimeKind.Utc);

        (await admin.PostAsJsonAsync($"/api/v1/sections/{first.Section.Id}/sessions", new SessionRequest(start, start.AddHours(2), null), Json))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        await (await admin.PostAsJsonAsync($"/api/v1/sections/{second.Section.Id}/sessions", new SessionRequest(start.AddHours(1), start.AddHours(3), null), Json))
            .ShouldBeRuleViolationAsync("BR-16");

        // Back to back is permitted; end must follow start (400 from the contract validator).
        (await admin.PostAsJsonAsync($"/api/v1/sections/{second.Section.Id}/sessions", new SessionRequest(start.AddHours(2), start.AddHours(4), null), Json))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        await (await admin.PostAsJsonAsync($"/api/v1/sections/{second.Section.Id}/sessions", new SessionRequest(start, start, null), Json))
            .ShouldBeFieldValidationErrorAsync(nameof(SessionRequest.ScheduledEndUtc));
    }

    // ----- Input validation, conflicts and cross-module references (13.5) -----

    [Fact]
    public async Task Create_WithInvalidInput_Returns400FieldKeyed_AndDuplicateCodes_Return409()
    {
        var (admin, _) = await ClientAsAsync(factory, RoleNames.Administrator);

        await (await admin.PostAsJsonAsync("/api/v1/programmes", new CreateProgrammeRequest("", "", "", 0), Json))
            .ShouldBeFieldValidationErrorAsync(nameof(CreateProgrammeRequest.Code));

        var programme = await CreateProgrammeAsync(admin);
        var duplicate = await admin.PostAsJsonAsync("/api/v1/programmes", new CreateProgrammeRequest(programme.Code, "Again", "مجدداً", 12), Json);
        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await duplicate.Content.ReadFromJsonAsync<ProblemDetails>(Json))!.Title.ShouldBe("programmes.code_taken");

        var course = await CreateCourseAsync(admin, programme.Id);
        (await admin.PostAsJsonAsync("/api/v1/courses", new CreateCourseRequest(programme.Id, course.Code, "Again", "مجدداً", null, null, 1), Json))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
        await (await admin.PostAsJsonAsync("/api/v1/courses", new CreateCourseRequest(Guid.NewGuid(), "NEW1", "New", "جديد", null, null, 1), Json))
            .ShouldBeFieldValidationErrorAsync(nameof(CreateCourseRequest.ProgrammeId));
    }

    [Fact]
    public async Task SectionInstructor_MustBeAnActiveInstructorAccount()
    {
        var (admin, _) = await ClientAsAsync(factory, RoleNames.Administrator);
        var course = await CreateCourseAsync(admin, (await CreateProgrammeAsync(admin)).Id);
        var learnerAccount = await AuthTestSupport.SeedUserAsync(factory, roles: RoleNames.Learner);
        var inactiveInstructor = await AuthTestSupport.SeedUserAsync(factory, active: false, roles: RoleNames.Instructor);

        CreateSectionRequest Request(Guid instructor) =>
            new(course.Id, Unique("S"), "T", new DateOnly(2026, 9, 1), new DateOnly(2026, 12, 1), 10, instructor, DeliveryMode.Online);

        await (await admin.PostAsJsonAsync("/api/v1/sections", Request(Guid.NewGuid()), Json)).ShouldBeFieldValidationErrorAsync("InstructorUserId");
        await (await admin.PostAsJsonAsync("/api/v1/sections", Request(learnerAccount.Id), Json)).ShouldBeFieldValidationErrorAsync("InstructorUserId");
        await (await admin.PostAsJsonAsync("/api/v1/sections", Request(inactiveInstructor.Id), Json)).ShouldBeFieldValidationErrorAsync("InstructorUserId");
    }

    [Fact]
    public async Task Learner_RequiresLearnerRoleAccount_AndOneRecordPerAccount()
    {
        var (admin, _) = await ClientAsAsync(factory, RoleNames.Administrator);
        var instructorAccount = await AuthTestSupport.SeedUserAsync(factory, roles: RoleNames.Instructor);

        await (await admin.PostAsJsonAsync("/api/v1/learners", new CreateLearnerRequest(instructorAccount.Id, "LX1", null, null, Gender.Male, null), Json))
            .ShouldBeFieldValidationErrorAsync("UserId");

        var (learner, user) = await CreateLearnerAsync(factory, admin);
        (await admin.PostAsJsonAsync("/api/v1/learners", new CreateLearnerRequest(user.Id, "LX2", null, null, Gender.Male, null), Json))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);

        // National identifier appears on single retrieval, never on the list (18.1).
        await ReadAsync<LearnerResponse>(await admin.PutAsJsonAsync($"/api/v1/learners/{learner.Id}",
            new UpdateLearnerRequest("1234567890", learner.DateOfBirth, Gender.Female, null, LearnerStatus.Active), Json));
        (await ReadAsync<LearnerResponse>(await admin.GetAsync($"/api/v1/learners/{learner.Id}"))).NationalId.ShouldBe("1234567890");
        var list = await ReadAsync<PagedResponse<LearnerResponse>>(await admin.GetAsync($"/api/v1/learners?search={learner.LearnerNumber}"));
        list.Items.ShouldHaveSingleItem().NationalId.ShouldBeNull();
        list.Items[0].User!.UserName.ShouldBe(user.UserName);
    }

    [Fact]
    public async Task CollectionEndpoints_PageServerSide_WithTheApi03Envelope()
    {
        var (admin, _) = await ClientAsAsync(factory, RoleNames.Administrator);
        var programme = await CreateProgrammeAsync(admin);
        for (var i = 0; i < 3; i++)
        {
            await CreateCourseAsync(admin, programme.Id);
        }

        var page = await ReadAsync<PagedResponse<CourseResponse>>(await admin.GetAsync($"/api/v1/courses?programmeId={programme.Id}&pageSize=2&sort=-code"));
        page.Page.ShouldBe(1);
        page.PageSize.ShouldBe(2);
        page.TotalCount.ShouldBe(3);
        page.TotalPages.ShouldBe(2);
        page.Items.Count.ShouldBe(2);
        string.CompareOrdinal(page.Items[0].Code, page.Items[1].Code).ShouldBeGreaterThan(0);

        (await admin.GetAsync("/api/v1/courses?sort=nope")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.GetAsync("/api/v1/enrolments")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
