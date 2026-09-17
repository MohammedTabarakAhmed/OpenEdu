using OpenCampus.Lms.Application.Abstractions;
using OpenCampus.Lms.Application.Assessment;
using OpenCampus.Lms.Application.Content;
using OpenCampus.Lms.Domain.Attendance;
using OpenCampus.SharedKernel;
using static OpenCampus.UnitTests.Lms.Application.LmsHarness;

namespace OpenCampus.UnitTests.Lms.Application;

/// <summary>Assessment services with in-memory collaborators: SEC-12 scope, BR-08/BR-09 through the service, file handling, attendance and the BR-12 report.</summary>
public class AssessmentServiceTests
{
    private static readonly DateTime Due = new(2026, 10, 15, 23, 59, 0, DateTimeKind.Utc);

    private static AssignmentRequest Request(bool allowLate = false, decimal penalty = 0m) =>
        new("Essay", "مقال", "Write 500 words.", 100m, Due, allowLate, penalty);

    private static async Task<AssignmentResponse> PublishedAsync(LmsHarness h, bool allowLate = false, decimal penalty = 0m)
    {
        var created = (await h.As(AssignedInstructor).Assignments.CreateAsync(h.Section.Id, Request(allowLate, penalty), CancellationToken.None)).Value;
        return (await h.Assignments.PublishAsync(created.Id, CancellationToken.None)).Value;
    }

    // ----- Scope (SEC-12) -----

    [Fact]
    public async Task OnlyManagers_CreatePublishAndDelete_OthersAreNotFound()
    {
        var h = new LmsHarness();

        foreach (var caller in new[] { EnrolledLearnerId, OtherInstructor, OtherLearner })
        {
            (await h.As(caller).Assignments.CreateAsync(h.Section.Id, Request(), CancellationToken.None)).Error.Type.ShouldBe(ErrorType.NotFound);
        }

        var assignment = await PublishedAsync(h);
        (await h.As(OtherInstructor).Assignments.UpdateAsync(assignment.Id, Request(), CancellationToken.None)).Error.Type.ShouldBe(ErrorType.NotFound);
        (await h.As(EnrolledLearnerId).Assignments.ListSubmissionsAsync(assignment.Id, CancellationToken.None)).Error.Type.ShouldBe(ErrorType.NotFound);
        (await h.As(OtherInstructor).Assignments.DeleteAsync(assignment.Id, CancellationToken.None)).Error.Type.ShouldBe(ErrorType.NotFound);
        (await h.AsAdministrator().Assignments.DeleteAsync(assignment.Id, CancellationToken.None)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Learner_SeesPublishedAssignmentsOnly_WithOwnSubmission()
    {
        var h = new LmsHarness();
        var published = await PublishedAsync(h);
        var draft = (await h.As(AssignedInstructor).Assignments.CreateAsync(h.Section.Id, Request() with { TitleEn = "Draft" }, CancellationToken.None)).Value;

        var manager = (await h.As(AssignedInstructor).Assignments.ListForSectionAsync(h.Section.Id, CancellationToken.None)).Value;
        manager.CanManage.ShouldBeTrue();
        manager.Assignments.Count.ShouldBe(2);

        var learner = (await h.As(EnrolledLearnerId).Assignments.ListForSectionAsync(h.Section.Id, CancellationToken.None)).Value;
        learner.CanManage.ShouldBeFalse();
        learner.Assignments.Select(a => a.Id).ShouldBe([published.Id]);
        learner.Assignments[0].MySubmission.ShouldBeNull();
        (await h.Assignments.GetAsync(draft.Id, CancellationToken.None)).Error.Type.ShouldBe(ErrorType.NotFound);

        (await h.As(OtherLearner).Assignments.ListForSectionAsync(h.Section.Id, CancellationToken.None)).Error.Type.ShouldBe(ErrorType.NotFound);
        (await h.As(OtherInstructor).Assignments.GetAsync(published.Id, CancellationToken.None)).Error.Type.ShouldBe(ErrorType.NotFound);
    }

    // ----- Submission: BR-09, BR-08, files -----

    [Fact]
    public async Task Submit_ByNonEnrolledLearner_IsNotFound_AndByManagerIsRefused_BR09()
    {
        var h = new LmsHarness();
        var assignment = await PublishedAsync(h);

        (await h.As(OtherLearner).Assignments.SubmitAsync(assignment.Id, new SubmitWorkRequest("x"), null, CancellationToken.None)).Error.Type.ShouldBe(ErrorType.NotFound);
        (await h.As(AssignedInstructor).Assignments.SubmitAsync(assignment.Id, new SubmitWorkRequest("x"), null, CancellationToken.None)).Error.Type.ShouldBe(ErrorType.NotFound);
        h.AssignmentRepository.Items.Single().Submissions.ShouldBeEmpty();
    }

    [Fact]
    public async Task Submit_LateWhenDisallowed_IsBR08_AndOnTimeSucceeds()
    {
        var h = new LmsHarness();
        var assignment = await PublishedAsync(h);

        h.Clock.Now = Due.AddMinutes(1);
        var late = await Should.ThrowAsync<BusinessRuleViolationException>(() =>
            h.As(EnrolledLearnerId).Assignments.SubmitAsync(assignment.Id, new SubmitWorkRequest("late"), null, CancellationToken.None));
        late.RuleCode.ShouldBe("BR-08");

        h.Clock.Now = Due.AddHours(-1);
        var result = await h.Assignments.SubmitAsync(assignment.Id, new SubmitWorkRequest("on time"), null, CancellationToken.None);
        result.IsSuccess.ShouldBeTrue();
        result.Value.IsLate.ShouldBeFalse();
        result.Value.LearnerUserId.ShouldBe(EnrolledLearnerId);

        var mine = (await h.Assignments.GetAsync(assignment.Id, CancellationToken.None)).Value.MySubmission;
        mine!.TextBody.ShouldBe("on time");
    }

    [Fact]
    public async Task Submit_WithFile_StoresUnderTheSubmissionHierarchy_AndReplacementDeletesTheOldFile()
    {
        var h = new LmsHarness();
        var assignment = await PublishedAsync(h);
        h.Clock.Now = Due.AddHours(-1);
        h.As(EnrolledLearnerId);

        var first = await h.Assignments.SubmitAsync(assignment.Id, new SubmitWorkRequest(null), Upload("essay v1.pdf", 100), CancellationToken.None);
        first.IsSuccess.ShouldBeTrue();
        first.Value.HasFile.ShouldBeTrue();
        h.Store.Files.Keys.ShouldHaveSingleItem().ShouldStartWith($"submissions/{assignment.Id:N}/{EnrolledLearnerId:N}/");

        var second = await h.Assignments.SubmitAsync(assignment.Id, new SubmitWorkRequest("with text"), Upload("essay v2.pdf", 120), CancellationToken.None);
        second.IsSuccess.ShouldBeTrue();
        h.Store.Files.Count.ShouldBe(1); // the replaced file is gone

        // Text-only resubmission keeps the existing file.
        var third = await h.Assignments.SubmitAsync(assignment.Id, new SubmitWorkRequest("text only"), null, CancellationToken.None);
        third.Value.HasFile.ShouldBeTrue();
        h.Store.Files.Count.ShouldBe(1);

        // Owner and manager may open the file; a stranger may not.
        (await h.Assignments.OpenSubmissionFileAsync(third.Value.Id, CancellationToken.None)).IsSuccess.ShouldBeTrue();
        (await h.As(AssignedInstructor).Assignments.OpenSubmissionFileAsync(third.Value.Id, CancellationToken.None)).IsSuccess.ShouldBeTrue();
        (await h.As(OtherLearner).Assignments.OpenSubmissionFileAsync(third.Value.Id, CancellationToken.None)).Error.Type.ShouldBe(ErrorType.NotFound);
        (await h.As(OtherInstructor).Assignments.OpenSubmissionFileAsync(third.Value.Id, CancellationToken.None)).Error.Type.ShouldBe(ErrorType.NotFound);
    }

    [Fact]
    public async Task Submit_WithDisallowedFile_IsRefused_SEC22_AndNothingStored()
    {
        var h = new LmsHarness();
        var assignment = await PublishedAsync(h);
        h.Clock.Now = Due.AddHours(-1);

        var result = await h.As(EnrolledLearnerId).Assignments.SubmitAsync(assignment.Id, new SubmitWorkRequest(null), Upload("virus.exe", 10), CancellationToken.None);

        result.Error.Type.ShouldBe(ErrorType.Validation);
        h.Store.Files.ShouldBeEmpty();
        h.AssignmentRepository.Items.Single().Submissions.ShouldBeEmpty();
    }

    // ----- Marking -----

    [Fact]
    public async Task Mark_ByManagerOnly_AppliesLatePenalty_AndListsAgainstEnrolledLearners()
    {
        var h = new LmsHarness();
        var assignment = await PublishedAsync(h, allowLate: true, penalty: 25m);
        h.Clock.Now = Due.AddHours(2);
        var submission = (await h.As(EnrolledLearnerId).Assignments.SubmitAsync(assignment.Id, new SubmitWorkRequest("late"), null, CancellationToken.None)).Value;
        submission.IsLate.ShouldBeTrue();

        (await h.As(EnrolledLearnerId).Assignments.MarkAsync(submission.Id, new MarkSubmissionRequest(80m, null), CancellationToken.None)).Error.Type.ShouldBe(ErrorType.NotFound);
        (await h.As(OtherInstructor).Assignments.MarkAsync(submission.Id, new MarkSubmissionRequest(80m, null), CancellationToken.None)).Error.Type.ShouldBe(ErrorType.NotFound);

        var marked = (await h.As(AssignedInstructor).Assignments.MarkAsync(submission.Id, new MarkSubmissionRequest(80m, "Late but good"), CancellationToken.None)).Value;
        marked.Score.ShouldBe(60m);
        marked.Feedback.ShouldBe("Late but good");

        var list = (await h.Assignments.ListSubmissionsAsync(assignment.Id, CancellationToken.None)).Value;
        list.EnrolledLearners.Select(l => l.UserId).ShouldBe([EnrolledLearnerId]);
        list.Submissions.Single().LearnerNumber.ShouldNotBeNull();
        list.Assignment.MarkedCount.ShouldBe(1);
    }

    // ----- Attendance: BR-13 and the BR-12 report -----

    private static (SessionSummary Past, SessionSummary Future) AddSessions(LmsHarness h)
    {
        var past = new SessionSummary(Guid.NewGuid(), h.Section.Id, h.Clock.Now.AddDays(-1), h.Clock.Now.AddDays(-1).AddHours(2), "Room 1");
        var future = new SessionSummary(Guid.NewGuid(), h.Section.Id, h.Clock.Now.AddDays(1), h.Clock.Now.AddDays(1).AddHours(2), "Room 1");
        h.Access.Sessions.AddRange([past, future]);
        return (past, future);
    }

    [Fact]
    public async Task RecordAttendance_ForFutureSession_IsBR13_AndForPastSession_ReportsTheRate()
    {
        var h = new LmsHarness();
        var (past, future) = AddSessions(h);
        h.As(AssignedInstructor);

        var violation = await Should.ThrowAsync<BusinessRuleViolationException>(() =>
            h.Attendance.RecordAsync(h.Section.Id, future.Id, new RecordAttendanceRequest([new AttendanceEntry(EnrolledLearnerId, AttendanceStatus.Present)]), CancellationToken.None));
        violation.RuleCode.ShouldBe("BR-13");
        h.AttendanceRepository.Items.ShouldBeEmpty();

        var register = (await h.Attendance.RecordAsync(h.Section.Id, past.Id, new RecordAttendanceRequest([new AttendanceEntry(EnrolledLearnerId, AttendanceStatus.Absent)]), CancellationToken.None)).Value;
        register.Records.Single().Status.ShouldBe(AttendanceStatus.Absent);
        h.Outcomes.Reports.ShouldBe([(h.Section.Id, EnrolledLearnerId, 0m)]); // 0 of 1 session held

        // Amending to Late counts as attended: 1 of 1.
        await h.Attendance.RecordAsync(h.Section.Id, past.Id, new RecordAttendanceRequest([new AttendanceEntry(EnrolledLearnerId, AttendanceStatus.Late)]), CancellationToken.None);
        h.Outcomes.Reports.Last().Percent.ShouldBe(100m);
        h.AttendanceRepository.Items.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task RecordAttendance_RejectsUnenrolledLearner_AndNonManagers()
    {
        var h = new LmsHarness();
        var (past, _) = AddSessions(h);

        var stranger = await h.As(AssignedInstructor).Attendance.RecordAsync(h.Section.Id, past.Id, new RecordAttendanceRequest([new AttendanceEntry(OtherLearner, AttendanceStatus.Present)]), CancellationToken.None);
        stranger.Error.Type.ShouldBe(ErrorType.Validation);

        foreach (var caller in new[] { EnrolledLearnerId, OtherInstructor })
        {
            (await h.As(caller).Attendance.RecordAsync(h.Section.Id, past.Id, new RecordAttendanceRequest([new AttendanceEntry(EnrolledLearnerId, AttendanceStatus.Present)]), CancellationToken.None)).Error.Type.ShouldBe(ErrorType.NotFound);
            (await h.Attendance.GetRegisterAsync(h.Section.Id, past.Id, CancellationToken.None)).Error.Type.ShouldBe(ErrorType.NotFound);
        }

        (await h.As(AssignedInstructor).Attendance.GetRegisterAsync(h.Section.Id, Guid.NewGuid(), CancellationToken.None)).Error.Type.ShouldBe(ErrorType.NotFound);
    }

    [Fact]
    public async Task SectionAttendance_ManagerSeesEveryone_LearnerSeesOnlyThemselves()
    {
        var h = new LmsHarness();
        var (past, _) = AddSessions(h);
        var second = Guid.NewGuid();
        h.Access.Enrolments.Add((second, h.Section.Id));
        await h.As(AssignedInstructor).Attendance.RecordAsync(h.Section.Id, past.Id,
            new RecordAttendanceRequest([new AttendanceEntry(EnrolledLearnerId, AttendanceStatus.Present), new AttendanceEntry(second, AttendanceStatus.Absent)]), CancellationToken.None);

        var manager = (await h.Attendance.GetForSectionAsync(h.Section.Id, CancellationToken.None)).Value;
        manager.CanManage.ShouldBeTrue();
        manager.Learners.Count.ShouldBe(2);
        manager.Records.Count.ShouldBe(2);
        manager.Learners.Single(l => l.LearnerUserId == second).AttendancePercent.ShouldBe(0m);

        var learner = (await h.As(EnrolledLearnerId).Attendance.GetForSectionAsync(h.Section.Id, CancellationToken.None)).Value;
        learner.CanManage.ShouldBeFalse();
        learner.Learners.ShouldHaveSingleItem().AttendancePercent.ShouldBe(100m);
        learner.Records.ShouldHaveSingleItem().LearnerUserId.ShouldBe(EnrolledLearnerId);

        (await h.As(OtherLearner).Attendance.GetForSectionAsync(h.Section.Id, CancellationToken.None)).Error.Type.ShouldBe(ErrorType.NotFound);
    }

    [Fact]
    public void Summarise_CountsOnlySessionsWithARegister()
    {
        var sectionId = Guid.NewGuid();
        var learner = Guid.NewGuid();
        var other = Guid.NewGuid();
        var s1 = new SessionSummary(Guid.NewGuid(), sectionId, Due, Due.AddHours(1), null);
        var s2 = new SessionSummary(Guid.NewGuid(), sectionId, Due.AddDays(1), Due.AddDays(1).AddHours(1), null);
        var s3 = new SessionSummary(Guid.NewGuid(), sectionId, Due.AddDays(2), Due.AddDays(2).AddHours(1), null); // no register yet
        var records = new List<AttendanceRecord>
        {
            AttendanceRecord.Create(s1.Id, s1.ScheduledStartUtc, learner, AttendanceStatus.Present, other, Due.AddDays(3)),
            AttendanceRecord.Create(s2.Id, s2.ScheduledStartUtc, other, AttendanceStatus.Present, other, Due.AddDays(3)), // learner unrecorded for s2
        };

        var summary = AttendanceService.Summarise(learner, [s1, s2, s3], records);

        summary.SessionsHeld.ShouldBe(2);
        summary.SessionsAttended.ShouldBe(1);
        summary.AttendancePercent.ShouldBe(50m);
        AttendanceService.Summarise(learner, [s3], []).AttendancePercent.ShouldBe(100m); // nothing held yet
    }
}
