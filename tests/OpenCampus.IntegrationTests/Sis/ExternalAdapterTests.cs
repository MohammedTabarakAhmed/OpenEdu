using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using OpenCampus.IntegrationTests.Lms;
using OpenCampus.Lms.Application.Abstractions;
using OpenCampus.Sis.Application.Certificates;
using OpenCampus.Sis.Application.External;
using OpenCampus.Sis.Application.Grading;
using LmsExternal = OpenCampus.Lms.Application.External;

namespace OpenCampus.IntegrationTests.Sis;

/// <summary>
/// The local external adapters (8.3, 18.6) through the real host: each contract answers with success, partial and
/// total failure and leaves inspectable evidence beneath the configured output directory; and the mandatory
/// consumers fire on the real flows — grade release (EXT-01), certificate issuance (EXT-01 + EXT-05) and the BR-12
/// At-Risk transition (EXT-02) — without any of them being able to fail the business change.
/// </summary>
[Collection(ApiCollection.Name)]
public class ExternalAdapterTests(ApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private IEnumerable<JsonElement> Lines(string file) =>
        File.Exists(Path.Combine(factory.NotificationRoot, file))
            ? File.ReadLines(Path.Combine(factory.NotificationRoot, file)).Where(l => l.Length > 0).Select(l => JsonSerializer.Deserialize<JsonElement>(l, Json))
            : [];

    [Fact]
    public async Task EmailAndSms_AnswerSuccessPartialAndTotalFailure_AndPersistEvidence()
    {
        var email = factory.Services.GetRequiredService<IEmailDispatcher>();
        var sms = factory.Services.GetRequiredService<ISmsDispatcher>();
        var marker = Guid.NewGuid().ToString("N");

        var ok = await email.SendAsync(new EmailMessage(["a@example.test", "b@example.test"], $"subject {marker}", "body"), CancellationToken.None);
        ok.Outcome.ShouldBe(ExternalOutcome.Succeeded);
        ok.Reference.ShouldStartWith("email-");
        ok.Failures.ShouldBeEmpty();

        var partial = await email.SendAsync(new EmailMessage(["a@example.test", "not an address"], $"partial {marker}", "body"), CancellationToken.None);
        partial.Outcome.ShouldBe(ExternalOutcome.PartiallySucceeded);
        partial.Failures.Single().Target.ShouldBe("not an address");

        var failed = await email.SendAsync(new EmailMessage([""], "nothing", "body"), CancellationToken.None);
        failed.Outcome.ShouldBe(ExternalOutcome.Failed);
        failed.Reference.ShouldBeNull();

        var smsOk = await sms.SendAsync(new SmsMessage(["+966500000001"], $"text {marker}"), CancellationToken.None);
        smsOk.Outcome.ShouldBe(ExternalOutcome.Succeeded);
        (await sms.SendAsync(new SmsMessage(["call me maybe"], "x"), CancellationToken.None)).Outcome.ShouldBe(ExternalOutcome.Failed);

        // Evidence: the accepted messages are on disk with sender identity and the rejected targets; failed dispatches leave nothing.
        var emails = Lines("email.jsonl").Where(e => e.GetProperty("subject").GetString()!.Contains(marker)).ToList();
        emails.Count.ShouldBe(2);
        emails[0].GetProperty("from").GetString().ShouldNotBeNullOrWhiteSpace();
        emails[1].GetProperty("rejected").GetArrayLength().ShouldBe(1);
        Lines("email.jsonl").ShouldNotContain(e => e.GetProperty("subject").GetString() == "nothing");
        Lines("sms.jsonl").ShouldContain(e => e.GetProperty("text").GetString()!.Contains(marker));
    }

    [Fact]
    public async Task ArchiveAndLibrary_SatisfyTheirContracts()
    {
        var archive = factory.Services.GetRequiredService<IRecordsArchive>();
        var library = factory.Services.GetRequiredService<ILibraryCatalogue>();
        var subject = Guid.NewGuid();

        var stored = await archive.ArchiveAsync(new ArchiveRecord("transcript", subject, """{"learner":"L-1","credits":12}"""), CancellationToken.None);
        stored.Outcome.ShouldBe(ExternalOutcome.Succeeded);
        var path = Path.Combine(factory.NotificationRoot, "archive", "transcript", $"{subject:N}.json");
        File.Exists(path).ShouldBeTrue();
        (await File.ReadAllTextAsync(path)).ShouldContain("\"credits\": 12");

        (await archive.ArchiveAsync(new ArchiveRecord("transcript", subject, "{not json"), CancellationToken.None)).Outcome.ShouldBe(ExternalOutcome.Failed);
        (await archive.ArchiveAsync(new ArchiveRecord("../escape", subject, "{}"), CancellationToken.None)).Outcome.ShouldBe(ExternalOutcome.Failed);

        var found = await library.SearchAsync("algorithms", 10, CancellationToken.None);
        found.Outcome.ShouldBe(ExternalOutcome.Succeeded);
        found.Items.ShouldContain(i => i.Title == "Introduction to Algorithms");
        (await library.SearchAsync("Martin", 1, CancellationToken.None)).Items.Count.ShouldBe(1);
        (await library.SearchAsync(" ", 10, CancellationToken.None)).Outcome.ShouldBe(ExternalOutcome.Failed);
    }

    [Fact]
    public async Task OriginalityChecker_IsDeterministic_AndRefusesBlankText()
    {
        var checker = factory.Services.GetRequiredService<LmsExternal.IOriginalityChecker>();
        var id = Guid.NewGuid();

        var first = await checker.CheckAsync(new LmsExternal.OriginalityRequest(id, "The quick brown fox."), CancellationToken.None);
        var again = await checker.CheckAsync(new LmsExternal.OriginalityRequest(id, "  the QUICK brown fox. "), CancellationToken.None);

        first.Outcome.ShouldBe(LmsExternal.ExternalOutcome.Succeeded);
        first.SimilarityPercent.ShouldNotBeNull();
        first.SimilarityPercent.Value.ShouldBeInRange(0, 100);
        again.SimilarityPercent.ShouldBe(first.SimilarityPercent);
        again.Reference.ShouldBe(first.Reference);
        (await checker.CheckAsync(new LmsExternal.OriginalityRequest(id, "   "), CancellationToken.None)).Outcome.ShouldBe(LmsExternal.ExternalOutcome.Failed);

        // The LMS's own e-mail adapter writes to the same evidence file.
        var lmsEmail = factory.Services.GetRequiredService<LmsExternal.IEmailDispatcher>();
        (await lmsEmail.SendAsync(new LmsExternal.EmailMessage(["x@example.test"], "lms-origin", "b"), CancellationToken.None)).Outcome.ShouldBe(LmsExternal.ExternalOutcome.Succeeded);
        Lines("email.jsonl").ShouldContain(e => e.GetProperty("subject").GetString() == "lms-origin" && e.GetProperty("origin").GetString() == "lms");
    }

    [Fact]
    public async Task Release_Issue_AndAtRisk_NotifyThroughTheAdapters()
    {
        var cast = await LmsTestSupport.BuildSectionCastAsync(factory);
        var section = await SisTestSupport.ReadAsync<OpenCampus.Sis.Application.Sections.SectionDetailResponse>(await cast.Admin.GetAsync($"/api/v1/sections/{cast.SectionId}"));
        var enrolments = await SisTestSupport.ReadAsync<OpenCampus.SharedKernel.PagedResponse<OpenCampus.Sis.Application.Enrolments.EnrolmentResponse>>(
            await cast.Admin.GetAsync($"/api/v1/sections/{cast.SectionId}/enrolments"));
        var enrolmentId = enrolments.Items.Single().Id;
        var learnerEmail = cast.LearnerUser.Email;

        // EXT-02 on the BR-12 transition, through the LMS→SIS contract; the reverse transition sends nothing.
        using (var scope = factory.Services.CreateScope())
        {
            var outcomes = scope.ServiceProvider.GetRequiredService<IAssessmentOutcomes>();
            await outcomes.ReportAttendanceRateAsync(cast.SectionId, cast.LearnerUser.Id, 10m, CancellationToken.None);
            await outcomes.ReportAttendanceRateAsync(cast.SectionId, cast.LearnerUser.Id, 100m, CancellationToken.None);
        }

        var smsToLearner = Lines("sms.jsonl").Where(e => e.GetProperty("text").GetString()!.Contains(section.Section.Code)).ToList();
        smsToLearner.Count.ShouldBe(1);
        smsToLearner[0].GetProperty("to")[0].GetString().ShouldBe("+966500000001");
        smsToLearner[0].GetProperty("text").GetString().ShouldNotBeNull().ShouldContain("10%");

        // EXT-01 on release.
        (await cast.Instructor.PostAsJsonAsync($"/api/v1/sections/{cast.SectionId}/grades", new RecordGradeRequest(enrolmentId, section.GradeComponents.Single().Id, 77m), SisTestSupport.Json))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await cast.Instructor.PostAsync($"/api/v1/sections/{cast.SectionId}/grades/release", null)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var releaseMail = Lines("email.jsonl").Where(e => e.GetProperty("subject").GetString()!.StartsWith("Grades released") && e.GetProperty("to").EnumerateArray().Any(t => t.GetString() == learnerEmail)).ToList();
        releaseMail.Count.ShouldBe(1);
        releaseMail[0].GetProperty("subject").GetString().ShouldNotBeNull().ShouldContain(section.Section.Code);

        // EXT-01 + EXT-05 on issuance.
        var issued = await SisTestSupport.ReadAsync<CertificateResponse>(await cast.Admin.PostAsync($"/api/v1/enrolments/{enrolmentId}/certificate", null), HttpStatusCode.Created);

        var certificateMail = Lines("email.jsonl").Where(e => e.GetProperty("subject").GetString()!.StartsWith("Your certificate") && e.GetProperty("to").EnumerateArray().Any(t => t.GetString() == learnerEmail)).ToList();
        certificateMail.Count.ShouldBe(1);
        certificateMail[0].GetProperty("body").GetString().ShouldNotBeNull().ShouldContain(issued.VerificationCode);

        var archived = Path.Combine(factory.NotificationRoot, "archive", "certificate", $"{issued.Id:N}.json");
        File.Exists(archived).ShouldBeTrue();
        (await File.ReadAllTextAsync(archived)).ShouldContain(issued.VerificationCode);
    }
}
