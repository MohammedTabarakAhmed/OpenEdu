using OpenCampus.SharedKernel;
using OpenCampus.Sis.Domain.Enrolments;
using static OpenCampus.UnitTests.Sis.Domain.SisFixtures;

namespace OpenCampus.UnitTests.Sis.Domain;

/// <summary>Section 14 rules BR-10 and BR-11, each proven by refusal of the violating case (TST-03).</summary>
public class CertificateRulesTests
{
    private static Enrolment CompletedEnrolment(decimal finalGrade)
    {
        var enrolment = Enrolment.Create(Learner(), Section(capacity: 10), 0, false, Now);
        enrolment.Complete(finalGrade, Now.AddDays(90));
        return enrolment;
    }

    [Fact]
    public void Issue_ForCompletedEnrolmentAtOrAboveThreshold_Succeeds()
    {
        var enrolment = CompletedEnrolment(finalGrade: 50m);

        var certificate = Certificate.Issue(enrolment, passThresholdPercent: 50m, alreadyIssued: false, "ABCDEF1234", "certificates/e1/cert.pdf", Now.AddDays(91));

        certificate.EnrolmentId.ShouldBe(enrolment.Id);
        certificate.VerificationCode.ShouldBe("ABCDEF1234");
        certificate.FilePath.ShouldBe("certificates/e1/cert.pdf");
        certificate.IssuedAtUtc.ShouldBe(Now.AddDays(91));
    }

    [Fact]
    public void BR10_Issue_WhenFinalGradeBelowThreshold_IsRefused()
    {
        var enrolment = CompletedEnrolment(finalGrade: 49.99m);

        Should.Throw<BusinessRuleViolationException>(() =>
                Certificate.Issue(enrolment, passThresholdPercent: 50m, alreadyIssued: false, "CODE", "path.pdf", Now))
            .RuleCode.ShouldBe("BR-10");
    }

    [Theory]
    [InlineData(EnrolmentStatus.Active)]
    [InlineData(EnrolmentStatus.AtRisk)]
    [InlineData(EnrolmentStatus.Withdrawn)]
    public void BR10_Issue_WhenEnrolmentNotCompleted_IsRefused(EnrolmentStatus status)
    {
        var enrolment = Enrolment.Create(Learner(), Section(capacity: 10), 0, false, Now);
        if (status == EnrolmentStatus.Withdrawn)
        {
            enrolment.Withdraw();
        }
        else if (status == EnrolmentStatus.AtRisk)
        {
            enrolment.ApplyAttendanceRate(0m, 75m);
        }

        Should.Throw<BusinessRuleViolationException>(() =>
                Certificate.Issue(enrolment, passThresholdPercent: 50m, alreadyIssued: false, "CODE", "path.pdf", Now))
            .RuleCode.ShouldBe("BR-10");
    }

    [Fact]
    public void BR11_Issue_WhenAlreadyIssued_IsRefused()
    {
        var enrolment = CompletedEnrolment(finalGrade: 80m);

        Should.Throw<BusinessRuleViolationException>(() =>
                Certificate.Issue(enrolment, passThresholdPercent: 50m, alreadyIssued: true, "CODE", "path.pdf", Now))
            .RuleCode.ShouldBe("BR-11");
    }

    [Fact]
    public void BR11_TakesPrecedenceOverBR10_WhenBothWouldRefuse()
    {
        var enrolment = Enrolment.Create(Learner(), Section(capacity: 10), 0, false, Now);

        Should.Throw<BusinessRuleViolationException>(() =>
                Certificate.Issue(enrolment, passThresholdPercent: 50m, alreadyIssued: true, "CODE", "path.pdf", Now))
            .RuleCode.ShouldBe("BR-11");
    }

    [Fact]
    public void Issue_WithBlankVerificationCode_IsRefused()
    {
        var enrolment = CompletedEnrolment(finalGrade: 80m);

        Should.Throw<DomainException>(() =>
            Certificate.Issue(enrolment, passThresholdPercent: 50m, alreadyIssued: false, "  ", "path.pdf", Now));
    }
}
