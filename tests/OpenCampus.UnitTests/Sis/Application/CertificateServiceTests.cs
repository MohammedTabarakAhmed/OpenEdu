using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenCampus.SharedKernel;
using OpenCampus.Sis.Application.Abstractions;
using OpenCampus.Sis.Application.Certificates;
using OpenCampus.Sis.Application.External;
using OpenCampus.Sis.Application.Grading;
using OpenCampus.Sis.Domain.Courses;
using OpenCampus.Sis.Domain.Enrolments;
using OpenCampus.Sis.Domain.Learners;
using OpenCampus.Sis.Domain.Programmes;
using OpenCampus.Sis.Domain.Sections;
using OpenCampus.UnitTests.Sis.Domain;

namespace OpenCampus.UnitTests.Sis.Application;

/// <summary>
/// Certificate issuance, scope and verification at the service level with in-memory collaborators: the rules stay in
/// the aggregate; here we prove the orchestration — nothing stored when refused, cleanup when the commit fails, the
/// audit record, the learner/administrator reach (SEC-12) and code normalisation on verification.
/// </summary>
public class CertificateServiceTests
{
    private static readonly DateTime Now = new(2026, 9, 17, 10, 0, 0, DateTimeKind.Utc);

    private sealed class Harness
    {
        public FakeStructure Structure { get; } = new();
        public FakeCertificateRepository Certificates { get; } = new();
        public FakeCertificateStore Store { get; } = new();
        public FakeRenderer Renderer { get; } = new();
        public FakeCodes Codes { get; } = new();
        public FakeAuditTrail Audit { get; } = new();
        public FakeUserDirectory Users { get; } = new();
        public FakeCurrentUser Caller { get; } = new();
        public FakeExternal External { get; } = new();

        public Programme Programme { get; }
        public Course Course { get; }
        public CourseSection Section { get; }

        public Harness()
        {
            Programme = Programme.Create("BSC-CS", "Computer Science", "علوم الحاسب", 36);
            Course = Course.Create(Programme.Id, "CS101", "Programming", "برمجة", null, null, 3);
            Section = CourseSection.Create(Course.Id, "cs101-a", "2026 Autumn", new DateOnly(2026, 9, 1), new DateOnly(2026, 12, 15), 10, Guid.NewGuid(), DeliveryMode.InPerson);
            Section.AddGradeComponent("Final", "نهائي", 100m, 100m);
            Section.Open();
            Structure.Programmes.Add(Programme);
            Structure.Courses.Add(Course);
            Structure.Sections.Add(Section);
        }

        public CertificateService Service(decimal passThreshold = 50m) =>
            new(Certificates, Structure, Structure, Structure, Structure, Structure, Users, Caller, Audit,
                new LearnerNotifier(External, External, Users, NullLogger<LearnerNotifier>.Instance), External, NullLogger<CertificateService>.Instance,
                Store, Renderer, Codes, Options.Create(new AcademicOptions { PassThresholdPercent = passThreshold }),
                Certificates, new FakeTime(Now));

        /// <summary>A learner with an enrolment in the section, completed at the given grade (or left active).</summary>
        public (Learner Learner, Enrolment Enrolment) AddLearner(decimal? finalGrade, string nameEn = "Amina Khalil", string nameAr = "أمينة خليل")
        {
            var learner = SisFixtures.Learner();
            Users.Add(learner.UserId, nameEn, nameAr);
            Structure.Learners.Add(learner);
            var enrolment = Enrolment.Create(learner, Section, 0, false, Now.AddDays(-100));
            if (finalGrade is { } grade)
            {
                enrolment.Complete(grade, Now.AddDays(-1));
            }

            Structure.Enrolments.Add(enrolment);
            return (learner, enrolment);
        }
    }

    private sealed class FakeTime(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }

    private static Harness Arrange() => new();

    [Fact]
    public async Task Issue_ForEligibleEnrolment_StoresDocument_PersistsRow_AndAudits()
    {
        var h = Arrange();
        var (learner, enrolment) = h.AddLearner(finalGrade: 82m);
        h.Codes.Next_.Enqueue("ABCDE-FGHJK-MNPQR-STUVW");
        h.Caller.Permissions.Add("sis.certificate.issue");

        var result = await h.Service().IssueAsync(enrolment.Id, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : null);
        result.Value.VerificationCode.ShouldBe("ABCDE-FGHJK-MNPQR-STUVW");
        result.Value.LearnerFullNameEn.ShouldBe("Amina Khalil");
        result.Value.LearnerFullNameAr.ShouldBe("أمينة خليل");
        result.Value.CourseCode.ShouldBe("CS101");
        result.Value.ProgrammeCode.ShouldBe("BSC-CS");
        result.Value.FinalGrade.ShouldBe(82m);
        result.Value.IssuedAtUtc.ShouldBe(Now);

        // 18.5: layout from the owning identifiers; the row stores the relative path only.
        var stored = h.Certificates.Items.ShouldHaveSingleItem();
        stored.FilePath.ShouldStartWith($"certificates/{learner.Id:N}/{enrolment.Id:N}/");
        h.Store.Files.ShouldContainKey(stored.FilePath);
        h.Certificates.SaveCount.ShouldBe(1);

        // What was printed matches what was certified.
        var document = h.Renderer.Rendered.ShouldHaveSingleItem();
        document.VerificationCode.ShouldBe("ABCDE-FGHJK-MNPQR-STUVW");
        document.FinalGradePercent.ShouldBe(82m);
        document.LearnerNumber.ShouldBe(learner.LearnerNumber);

        // SEC-30: one immutable record naming the certificate.
        var audit = h.Audit.Events.ShouldHaveSingleItem();
        audit.EventType.ShouldBe(SisAuditEventTypes.CertificateIssued);
        audit.EntityName.ShouldBe(nameof(Certificate));
        audit.EntityId.ShouldBe(stored.Id);
        audit.Details.ShouldNotBeNull().ShouldContain(enrolment.Id.ToString());

        // EXT-05: the award is archived under the certificate; EXT-01: the learner is told, with the code.
        var archived = h.External.Archived.ShouldHaveSingleItem();
        archived.RecordType.ShouldBe("certificate");
        archived.SubjectId.ShouldBe(stored.Id);
        archived.PayloadJson.ShouldContain("ABCDE-FGHJK-MNPQR-STUVW");
        var notice = h.External.Emails.ShouldHaveSingleItem();
        notice.To.ShouldBe([h.Users.Users[learner.UserId].Email]);
        notice.Body.ShouldContain("ABCDE-FGHJK-MNPQR-STUVW");
    }

    [Fact]
    public async Task Issue_WhenArchiveAndNoticeFail_StillSucceeds()
    {
        var h = Arrange();
        var (_, enrolment) = h.AddLearner(finalGrade: 82m);
        h.External.NextResult = ExternalResult.Failure([new ExternalFailure("provider", "unavailable")]);

        var result = await h.Service().IssueAsync(enrolment.Id, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        h.Certificates.Items.Count.ShouldBe(1);
        h.External.Archived.Count.ShouldBe(1);
        h.External.Emails.Count.ShouldBe(1);
    }

    [Fact]
    public async Task BR10_Issue_WhenNotEligible_RefusesBeforeAnythingIsRenderedOrStored()
    {
        var h = Arrange();
        var (_, active) = h.AddLearner(finalGrade: null);
        var (_, failed) = h.AddLearner(finalGrade: 40m);

        foreach (var enrolment in new[] { active, failed })
        {
            var violation = await Should.ThrowAsync<BusinessRuleViolationException>(() => h.Service().IssueAsync(enrolment.Id, CancellationToken.None));
            violation.RuleCode.ShouldBe("BR-10");
        }

        h.Renderer.Rendered.ShouldBeEmpty();
        h.Store.Files.ShouldBeEmpty();
        h.Certificates.Items.ShouldBeEmpty();
        h.Audit.Events.ShouldBeEmpty();
        h.External.Emails.ShouldBeEmpty();
        h.External.Archived.ShouldBeEmpty();
    }

    [Fact]
    public async Task BR11_Issue_Twice_IsRefused_AndTheFirstDocumentIsUntouched()
    {
        var h = Arrange();
        var (_, enrolment) = h.AddLearner(finalGrade: 75m);
        var first = await h.Service().IssueAsync(enrolment.Id, CancellationToken.None);
        first.IsSuccess.ShouldBeTrue();

        var violation = await Should.ThrowAsync<BusinessRuleViolationException>(() => h.Service().IssueAsync(enrolment.Id, CancellationToken.None));

        violation.RuleCode.ShouldBe("BR-11");
        h.Certificates.Items.Count.ShouldBe(1);
        h.Store.Files.Count.ShouldBe(1);
        h.Renderer.Rendered.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Issue_WhenCommitFails_RemovesTheStoredDocument_AndWritesNoAudit()
    {
        var h = Arrange();
        var (_, enrolment) = h.AddLearner(finalGrade: 90m);
        h.Certificates.FailNextSave = new InvalidOperationException("simulated database failure");

        await Should.ThrowAsync<InvalidOperationException>(() => h.Service().IssueAsync(enrolment.Id, CancellationToken.None));

        h.Store.Files.ShouldBeEmpty();
        h.Store.Deleted.ShouldHaveSingleItem();
        h.Certificates.Items.ShouldBeEmpty();
        h.Audit.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task Issue_UnknownEnrolment_IsNotFound()
    {
        var h = Arrange();

        var result = await h.Service().IssueAsync(Guid.NewGuid(), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Type.ShouldBe(ErrorType.NotFound);
    }

    [Fact]
    public async Task Issue_UsesThresholdFromConfiguration()
    {
        var h = Arrange();
        var (_, enrolment) = h.AddLearner(finalGrade: 60m);

        (await Should.ThrowAsync<BusinessRuleViolationException>(() => h.Service(passThreshold: 65m).IssueAsync(enrolment.Id, CancellationToken.None))).RuleCode.ShouldBe("BR-10");
        (await h.Service(passThreshold: 60m).IssueAsync(enrolment.Id, CancellationToken.None)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task SEC12_OwnLearnerAndAdministratorReachTheCertificate_AnotherLearnerDoesNot()
    {
        var h = Arrange();
        var (owner, enrolment) = h.AddLearner(finalGrade: 70m);
        var (other, _) = h.AddLearner(finalGrade: 70m, nameEn: "Bilal Saad", nameAr: "بلال سعد");
        var issued = (await h.Service().IssueAsync(enrolment.Id, CancellationToken.None)).Value;

        h.Caller.UserId = owner.UserId;
        (await h.Service().GetAsync(issued.Id, CancellationToken.None)).IsSuccess.ShouldBeTrue();
        var download = await h.Service().OpenAsync(issued.Id, CancellationToken.None);
        download.IsSuccess.ShouldBeTrue();
        download.Value.ContentType.ShouldBe("application/pdf");
        download.Value.FileName.ShouldBe($"certificate-{issued.VerificationCode}.pdf");
        await download.Value.Content.DisposeAsync();

        h.Caller.UserId = other.UserId;
        (await h.Service().GetAsync(issued.Id, CancellationToken.None)).Error.Type.ShouldBe(ErrorType.NotFound);
        (await h.Service().OpenAsync(issued.Id, CancellationToken.None)).Error.Type.ShouldBe(ErrorType.NotFound);
        (await h.Service().GetForEnrolmentAsync(enrolment.Id, CancellationToken.None)).Error.Type.ShouldBe(ErrorType.NotFound);

        h.Caller.UserId = Guid.NewGuid();
        h.Caller.Permissions.Add(CertificateService.LearnerAdministration);
        (await h.Service().GetAsync(issued.Id, CancellationToken.None)).IsSuccess.ShouldBeTrue();
        (await h.Service().GetForEnrolmentAsync(enrolment.Id, CancellationToken.None)).Value.Id.ShouldBe(issued.Id);
    }

    [Fact]
    public async Task Open_WhenDocumentIsMissingFromTheStore_IsNotFound()
    {
        var h = Arrange();
        var (owner, enrolment) = h.AddLearner(finalGrade: 70m);
        var issued = (await h.Service().IssueAsync(enrolment.Id, CancellationToken.None)).Value;
        h.Store.Files.Clear();
        h.Caller.UserId = owner.UserId;

        var result = await h.Service().OpenAsync(issued.Id, CancellationToken.None);

        result.Error.ShouldBe(OpenCampus.Sis.Application.SisErrors.CertificateDocumentMissing);
    }

    [Fact]
    public async Task Verify_MatchesAfterTrimmingAndUpperCasing_AndExposesNoIdentifiers()
    {
        var h = Arrange();
        var (_, enrolment) = h.AddLearner(finalGrade: 88m);
        h.Codes.Next_.Enqueue("ABCDE-FGHJK-MNPQR-STUVW");
        await h.Service().IssueAsync(enrolment.Id, CancellationToken.None);

        // Anonymous: no caller at all.
        var result = await h.Service().VerifyAsync("  abcde-fghjk-mnpqr-stuvw ", CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.VerificationCode.ShouldBe("ABCDE-FGHJK-MNPQR-STUVW");
        result.Value.LearnerFullNameEn.ShouldBe("Amina Khalil");
        result.Value.CourseCode.ShouldBe("CS101");
        result.Value.CompletedAtUtc.ShouldBe(Now.AddDays(-1));
        typeof(CertificateVerificationResponse).GetProperties().Select(p => p.PropertyType).ShouldNotContain(typeof(Guid));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ABCDE-FGHJK-MNPQR-STUVX")]
    [InlineData("THIS-CODE-IS-FAR-TOO-LONG-TO-BE-A-REAL-ONE-XXXX")]
    public async Task Verify_UnknownOrMalformedCode_IsNotFound(string code)
    {
        var h = Arrange();
        var (_, enrolment) = h.AddLearner(finalGrade: 88m);
        h.Codes.Next_.Enqueue("ABCDE-FGHJK-MNPQR-STUVW");
        await h.Service().IssueAsync(enrolment.Id, CancellationToken.None);

        var result = await h.Service().VerifyAsync(code, CancellationToken.None);

        result.Error.Type.ShouldBe(ErrorType.NotFound);
    }

    [Fact]
    public async Task Issue_SkipsAVerificationCodeAlreadyInUse()
    {
        var h = Arrange();
        var (_, first) = h.AddLearner(finalGrade: 70m);
        var (_, second) = h.AddLearner(finalGrade: 70m, nameEn: "Bilal Saad", nameAr: "بلال سعد");
        h.Codes.Next_.Enqueue("SAME1-SAME1-SAME1-SAME1");
        h.Codes.Next_.Enqueue("SAME1-SAME1-SAME1-SAME1");
        h.Codes.Next_.Enqueue("FRESH-FRESH-FRESH-FRESH");

        await h.Service().IssueAsync(first.Id, CancellationToken.None);
        var result = await h.Service().IssueAsync(second.Id, CancellationToken.None);

        result.Value.VerificationCode.ShouldBe("FRESH-FRESH-FRESH-FRESH");
    }
}
