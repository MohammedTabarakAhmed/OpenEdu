using System.Net;
using System.Net.Http.Json;
using OpenCampus.Identity.Application.Authorization;
using OpenCampus.IntegrationTests.Lms;
using OpenCampus.Sis.Application.Certificates;
using OpenCampus.Sis.Application.Grading;

namespace OpenCampus.IntegrationTests.Sis;

/// <summary>
/// AC-05 through the HTTP interface: a certificate is issued, downloaded and anonymously verified. Around it: BR-10
/// and BR-11 as 422 with the rule code (API-07), 201 + Location on issue (API-05), the 401/403/404 matrix for the
/// new routes (SEC-10/11/12) and the anonymous verification endpoint answering without a token (15.4).
/// </summary>
[Collection(ApiCollection.Name)]
public class CertificateApiTests(ApiFactory factory)
{
    private static async Task<(SectionCast Cast, Guid EnrolmentId, Guid ComponentId)> ArrangeAsync(ApiFactory factory)
    {
        var cast = await LmsTestSupport.BuildSectionCastAsync(factory);
        var section = await SisTestSupport.ReadAsync<OpenCampus.Sis.Application.Sections.SectionDetailResponse>(await cast.Admin.GetAsync($"/api/v1/sections/{cast.SectionId}"));
        var enrolments = await SisTestSupport.ReadAsync<OpenCampus.SharedKernel.PagedResponse<OpenCampus.Sis.Application.Enrolments.EnrolmentResponse>>(
            await cast.Admin.GetAsync($"/api/v1/sections/{cast.SectionId}/enrolments"));
        return (cast, enrolments.Items.Single().Id, section.GradeComponents.Single().Id);
    }

    private static async Task ReleaseAsync(SectionCast cast, Guid enrolmentId, Guid componentId, decimal score)
    {
        (await cast.Instructor.PostAsJsonAsync($"/api/v1/sections/{cast.SectionId}/grades", new RecordGradeRequest(enrolmentId, componentId, score), SisTestSupport.Json))
            .StatusCode.ShouldBe(HttpStatusCode.OK);
        (await cast.Instructor.PostAsync($"/api/v1/sections/{cast.SectionId}/grades/release", null)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AC05_CertificateIssued_Downloaded_AndAnonymouslyVerified()
    {
        var (cast, enrolmentId, componentId) = await ArrangeAsync(factory);

        // BR-10 before release: the enrolment is still Active.
        await (await cast.Admin.PostAsync($"/api/v1/enrolments/{enrolmentId}/certificate", null)).ShouldBeRuleViolationAsync("BR-10");

        await ReleaseAsync(cast, enrolmentId, componentId, 91m);

        // Issue: 201 with a Location that resolves (API-05).
        var issueResponse = await cast.Admin.PostAsync($"/api/v1/enrolments/{enrolmentId}/certificate", null);
        var issued = await SisTestSupport.ReadAsync<CertificateResponse>(issueResponse, HttpStatusCode.Created);
        issueResponse.Headers.Location.ShouldNotBeNull();
        issueResponse.Headers.Location.ToString().ShouldEndWith($"/api/v1/certificates/{issued.Id}");
        issued.EnrolmentId.ShouldBe(enrolmentId);
        issued.FinalGrade.ShouldBe(91m);
        issued.VerificationCode.Length.ShouldBe(23);

        // BR-11: issuing again is refused with the rule code (API-07).
        await (await cast.Admin.PostAsync($"/api/v1/enrolments/{enrolmentId}/certificate", null)).ShouldBeRuleViolationAsync("BR-11");

        // Administrative lookups by enrolment and by learner.
        (await SisTestSupport.ReadAsync<CertificateResponse>(await cast.Admin.GetAsync($"/api/v1/enrolments/{enrolmentId}/certificate"))).Id.ShouldBe(issued.Id);
        (await SisTestSupport.ReadAsync<List<CertificateResponse>>(await cast.Admin.GetAsync($"/api/v1/learners/{cast.LearnerRecord.Id}/certificates"))).Single().Id.ShouldBe(issued.Id);

        // The learner lists, retrieves and downloads their own certificate (15.3).
        var mine = await SisTestSupport.ReadAsync<List<CertificateResponse>>(await cast.Learner.GetAsync("/api/v1/me/certificates"));
        mine.Single().Id.ShouldBe(issued.Id);
        (await SisTestSupport.ReadAsync<CertificateResponse>(await cast.Learner.GetAsync($"/api/v1/certificates/{issued.Id}"))).VerificationCode.ShouldBe(issued.VerificationCode);

        var download = await cast.Learner.GetAsync($"/api/v1/certificates/{issued.Id}/file");
        download.StatusCode.ShouldBe(HttpStatusCode.OK);
        download.Content.Headers.ContentType!.MediaType.ShouldBe("application/pdf");
        download.Content.Headers.ContentDisposition!.FileName!.Trim('"').ShouldBe($"certificate-{issued.VerificationCode}.pdf");
        var bytes = await download.Content.ReadAsByteArrayAsync();
        System.Text.Encoding.ASCII.GetString(bytes, 0, 5).ShouldBe("%PDF-");

        // Anonymous verification: no bearer token at all; normalised code matches; the answer carries no identifiers.
        using var anonymous = factory.CreateClient();
        var verified = await SisTestSupport.ReadAsync<CertificateVerificationResponse>(
            await anonymous.GetAsync($"/api/v1/certificates/verify/{issued.VerificationCode.ToLowerInvariant()}"));
        verified.VerificationCode.ShouldBe(issued.VerificationCode);
        verified.LearnerFullNameEn.ShouldBe(issued.LearnerFullNameEn);
        verified.CourseCode.ShouldBe(issued.CourseCode);
        (await anonymous.GetAsync("/api/v1/certificates/verify/ZZZZZ-ZZZZZ-ZZZZZ-ZZZZZ")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task BR10_CompletedBelowThreshold_IsRefusedWith422()
    {
        var (cast, enrolmentId, componentId) = await ArrangeAsync(factory);
        await ReleaseAsync(cast, enrolmentId, componentId, 30m);

        await (await cast.Admin.PostAsync($"/api/v1/enrolments/{enrolmentId}/certificate", null)).ShouldBeRuleViolationAsync("BR-10");
        (await cast.Admin.GetAsync($"/api/v1/enrolments/{enrolmentId}/certificate")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AuthorizationMatrix_ForCertificateRoutes()
    {
        var (cast, enrolmentId, componentId) = await ArrangeAsync(factory);
        await ReleaseAsync(cast, enrolmentId, componentId, 80m);
        var (registrar, _) = await SisTestSupport.ClientAsAsync(factory, RoleNames.Registrar);

        // Issuance is an administrator capability (Appendix C: sis.certificate.issue): instructor, learner and registrar are forbidden (SEC-11).
        (await cast.Instructor.PostAsync($"/api/v1/enrolments/{enrolmentId}/certificate", null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await cast.Learner.PostAsync($"/api/v1/enrolments/{enrolmentId}/certificate", null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await registrar.PostAsync($"/api/v1/enrolments/{enrolmentId}/certificate", null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var issued = await SisTestSupport.ReadAsync<CertificateResponse>(await cast.Admin.PostAsync($"/api/v1/enrolments/{enrolmentId}/certificate", null), HttpStatusCode.Created);

        // Anonymous callers: every authenticated route is 401 (SEC-10); only verification answers.
        using var anonymous = factory.CreateClient();
        (await anonymous.GetAsync($"/api/v1/certificates/{issued.Id}")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync($"/api/v1/certificates/{issued.Id}/file")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync("/api/v1/me/certificates")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.PostAsync($"/api/v1/enrolments/{enrolmentId}/certificate", null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.GetAsync($"/api/v1/certificates/verify/{issued.VerificationCode}")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Another learner holds the permission but not the record: 404, indistinguishable from absence (SEC-12, API-06).
        (await cast.OtherLearner.GetAsync($"/api/v1/certificates/{issued.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await cast.OtherLearner.GetAsync($"/api/v1/certificates/{issued.Id}/file")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await SisTestSupport.ReadAsync<List<CertificateResponse>>(await cast.OtherLearner.GetAsync("/api/v1/me/certificates"))).ShouldBeEmpty();

        // Learner-role callers lack sis.learner.read: the administrative routes are forbidden outright.
        (await cast.Learner.GetAsync($"/api/v1/enrolments/{enrolmentId}/certificate")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await cast.Learner.GetAsync($"/api/v1/learners/{cast.LearnerRecord.Id}/certificates")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Registrar holds sis.learner.read: may look up and download, not issue.
        (await registrar.GetAsync($"/api/v1/learners/{cast.LearnerRecord.Id}/certificates")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await registrar.GetAsync($"/api/v1/certificates/{issued.Id}/file")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
