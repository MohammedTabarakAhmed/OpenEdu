using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using OpenCampus.Identity.Infrastructure.Persistence;
using OpenCampus.Identity.Infrastructure.Security;
using OpenCampus.IntegrationTests.Lms;
using OpenCampus.SharedKernel;
using OpenCampus.Sis.Application.Abstractions;
using OpenCampus.Sis.Application.Certificates;
using OpenCampus.Sis.Application.Grading;
using OpenCampus.Sis.Application.Sections;
using OpenCampus.Sis.Domain.Enrolments;
using OpenCampus.Sis.Infrastructure.Certificates;

namespace OpenCampus.IntegrationTests.Sis;

/// <summary>
/// Certificate issuance through the application service against the database, the local store and the real PDF
/// renderer, with the caller impersonated on the request context: release → issue → the learner lists, opens and
/// verifies it anonymously; BR-10 before release and BR-11 on a second issue; SEC-12 reach; SEC-30 audit record.
/// </summary>
[Collection(ApiCollection.Name)]
public class CertificateServiceTests(ApiFactory factory)
{
    private static IServiceScope ScopeAs(ApiFactory factory, Guid? userId, params string[] permissions)
    {
        var scope = factory.Services.CreateScope();
        var claims = new List<Claim>();
        if (userId is { } id)
        {
            claims.Add(new Claim(JwtRegisteredClaimNames.Sub, id.ToString()));
        }

        claims.AddRange(permissions.Select(p => new Claim(JwtAccessTokenIssuer.PermissionClaim, p)));
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext =
            new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, claims.Count > 0 ? "test" : null)) };
        return scope;
    }

    private static async Task<(SectionCast Cast, Guid EnrolmentId)> ArrangeAsync(ApiFactory factory)
    {
        var cast = await LmsTestSupport.BuildSectionCastAsync(factory);
        var enrolments = await SisTestSupport.ReadAsync<PagedResponse<OpenCampus.Sis.Application.Enrolments.EnrolmentResponse>>(
            await cast.Admin.GetAsync($"/api/v1/sections/{cast.SectionId}/enrolments"));
        return (cast, enrolments.Items.Single().Id);
    }

    /// <summary>Grades and releases the section so its one enrolment is Completed with the given final grade.</summary>
    private static async Task ReleaseAsync(ApiFactory factory, SectionCast cast, Guid enrolmentId, decimal score)
    {
        var section = await SisTestSupport.ReadAsync<SectionDetailResponse>(await cast.Admin.GetAsync($"/api/v1/sections/{cast.SectionId}"));
        using var scope = ScopeAs(factory, cast.InstructorUser.Id);
        var grading = scope.ServiceProvider.GetRequiredService<GradingService>();
        (await grading.RecordGradeAsync(cast.SectionId, new RecordGradeRequest(enrolmentId, section.GradeComponents.Single().Id, score), CancellationToken.None)).IsSuccess.ShouldBeTrue();
        (await grading.ReleaseAsync(cast.SectionId, CancellationToken.None)).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Administrator_Issues_LearnerListsOpensAndAnyoneVerifies_WithAuditRecord()
    {
        var (cast, enrolmentId) = await ArrangeAsync(factory);
        var adminUser = await Identity.AuthTestSupport.SeedUserAsync(factory, roles: OpenCampus.Identity.Application.Authorization.RoleNames.Administrator);

        // BR-10 through the service: nothing to certify before release.
        using (var scope = ScopeAs(factory, adminUser.Id, "sis.certificate.issue", CertificateService.LearnerAdministration))
        {
            var violation = await Should.ThrowAsync<BusinessRuleViolationException>(() =>
                scope.ServiceProvider.GetRequiredService<CertificateService>().IssueAsync(enrolmentId, CancellationToken.None));
            violation.RuleCode.ShouldBe("BR-10");
        }

        await ReleaseAsync(factory, cast, enrolmentId, 88m);

        CertificateResponse issued;
        using (var scope = ScopeAs(factory, adminUser.Id, "sis.certificate.issue", CertificateService.LearnerAdministration))
        {
            var service = scope.ServiceProvider.GetRequiredService<CertificateService>();
            var result = await service.IssueAsync(enrolmentId, CancellationToken.None);
            result.IsSuccess.ShouldBeTrue(result.IsFailure ? result.Error.Message : null);
            issued = result.Value;
            issued.EnrolmentId.ShouldBe(enrolmentId);
            issued.LearnerId.ShouldBe(cast.LearnerRecord.Id);
            issued.FinalGrade.ShouldBe(88m);
            issued.VerificationCode.Length.ShouldBe(23);

            // BR-11: a second issue for the same enrolment is refused.
            (await Should.ThrowAsync<BusinessRuleViolationException>(() => service.IssueAsync(enrolmentId, CancellationToken.None))).RuleCode.ShouldBe("BR-11");

            // Administrative reach by enrolment and by learner.
            (await service.GetForEnrolmentAsync(enrolmentId, CancellationToken.None)).Value.Id.ShouldBe(issued.Id);
            (await service.ListForLearnerAsync(cast.LearnerRecord.Id, CancellationToken.None)).Value.Single().Id.ShouldBe(issued.Id);
        }

        // 18.5: the row holds a relative path; the document sits beneath the configured root and is a PDF.
        using (var scope = factory.Services.CreateScope())
        {
            var store = (LocalCertificateStore)scope.ServiceProvider.GetRequiredService<ICertificateStore>();
            var certificate = await scope.ServiceProvider.GetRequiredService<ICertificateRepository>().FindByIdAsync(issued.Id, CancellationToken.None);
            certificate.ShouldNotBeNull();
            Path.IsPathRooted(certificate.FilePath).ShouldBeFalse();
            certificate.FilePath.ShouldStartWith($"certificates/{cast.LearnerRecord.Id:N}/{enrolmentId:N}/");
            var absolute = Path.Combine(store.RootPath, certificate.FilePath);
            File.Exists(absolute).ShouldBeTrue();
            var bytes = await File.ReadAllBytesAsync(absolute);
            Encoding.ASCII.GetString(bytes, 0, 5).ShouldBe("%PDF-");
            bytes.Length.ShouldBeGreaterThan(1000);
        }

        // The learner lists and opens their own certificate (15.3 "certificate listing/download").
        using (var scope = ScopeAs(factory, cast.LearnerUser.Id, "sis.enrolment.read"))
        {
            var service = scope.ServiceProvider.GetRequiredService<CertificateService>();
            var mine = (await service.ListMineAsync(CancellationToken.None)).Value;
            mine.Single().Id.ShouldBe(issued.Id);
            mine.Single().VerificationCode.ShouldBe(issued.VerificationCode);

            var download = (await service.OpenAsync(issued.Id, CancellationToken.None)).Value;
            download.ContentType.ShouldBe("application/pdf");
            download.FileName.ShouldBe($"certificate-{issued.VerificationCode}.pdf");
            await using var content = download.Content;
            var header = new byte[5];
            (await content.ReadAsync(header)).ShouldBe(5);
            Encoding.ASCII.GetString(header).ShouldBe("%PDF-");
        }

        // SEC-12: another learner cannot see, open or list it.
        using (var scope = ScopeAs(factory, cast.OtherLearnerUser.Id, "sis.enrolment.read"))
        {
            var service = scope.ServiceProvider.GetRequiredService<CertificateService>();
            (await service.GetAsync(issued.Id, CancellationToken.None)).Error.Type.ShouldBe(ErrorType.NotFound);
            (await service.OpenAsync(issued.Id, CancellationToken.None)).Error.Type.ShouldBe(ErrorType.NotFound);
            (await service.GetForEnrolmentAsync(enrolmentId, CancellationToken.None)).Error.Type.ShouldBe(ErrorType.NotFound);
            (await service.ListForLearnerAsync(cast.LearnerRecord.Id, CancellationToken.None)).Error.Type.ShouldBe(ErrorType.NotFound);
        }

        // Anonymous verification (15.4): no principal at all; the code is matched after normalisation.
        using (var scope = ScopeAs(factory, null))
        {
            var service = scope.ServiceProvider.GetRequiredService<CertificateService>();
            var verified = await service.VerifyAsync(issued.VerificationCode.ToLowerInvariant() + " ", CancellationToken.None);
            verified.IsSuccess.ShouldBeTrue();
            verified.Value.VerificationCode.ShouldBe(issued.VerificationCode);
            verified.Value.CourseCode.ShouldBe(issued.CourseCode);
            verified.Value.LearnerFullNameEn.ShouldBe(issued.LearnerFullNameEn);
            (await service.VerifyAsync("ZZZZZ-ZZZZZ-ZZZZZ-ZZZZZ", CancellationToken.None)).Error.Type.ShouldBe(ErrorType.NotFound);
        }

        // SEC-30: the issuance left one immutable record naming the certificate and the actor.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var record = await db.AuditEvents.SingleAsync(e => e.EventType == SisAuditEventTypes.CertificateIssued && e.EntityId == issued.Id);
            record.UserId.ShouldBe(adminUser.Id);
        }
    }

    [Fact]
    public async Task BR10_CompletedBelowThreshold_IsRefused_AndLeavesNoDocument()
    {
        var (cast, enrolmentId) = await ArrangeAsync(factory);
        await ReleaseAsync(factory, cast, enrolmentId, 35m); // Academic:PassThresholdPercent defaults to 50

        using var scope = ScopeAs(factory, Guid.NewGuid(), "sis.certificate.issue");
        var service = scope.ServiceProvider.GetRequiredService<CertificateService>();
        (await Should.ThrowAsync<BusinessRuleViolationException>(() => service.IssueAsync(enrolmentId, CancellationToken.None))).RuleCode.ShouldBe("BR-10");

        var store = (LocalCertificateStore)scope.ServiceProvider.GetRequiredService<ICertificateStore>();
        Directory.Exists(Path.Combine(store.RootPath, "certificates", cast.LearnerRecord.Id.ToString("N"))).ShouldBeFalse();
        (await scope.ServiceProvider.GetRequiredService<ICertificateRepository>().FindByEnrolmentAsync(enrolmentId, CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public void VerificationCodes_AreGroupedUnambiguousAndUnique()
    {
        var generator = new VerificationCodeGenerator();
        var codes = Enumerable.Range(0, 500).Select(_ => generator.Next()).ToList();

        codes.ShouldAllBe(c => System.Text.RegularExpressions.Regex.IsMatch(c, "^[A-HJ-NP-Z2-9]{5}(-[A-HJ-NP-Z2-9]{5}){3}$"));
        codes.Distinct().Count().ShouldBe(codes.Count);
        codes.ShouldAllBe(c => c.Length <= Certificate.VerificationCodeMaxLength);
    }
}
