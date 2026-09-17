using Microsoft.Extensions.Options;
using OpenCampus.Sis.Application.Grading;
using OpenCampus.Sis.Domain.Sections;

namespace OpenCampus.Sis.Application.Reports;

/// <summary>Filter shared by both reports; every part is optional and narrows the set of sections reported on.</summary>
public sealed record ReportFilter(Guid? ProgrammeId, Guid? CourseId, string? TermName, SectionStatus? Status);

/// <summary>Participation per section (BO-04): who is enrolled and in what standing, against capacity.</summary>
public sealed record ParticipationRow(
    Guid SectionId, string SectionCode, string TermName, SectionStatus SectionStatus, int Capacity,
    Guid CourseId, string CourseCode, string CourseNameEn, string CourseNameAr,
    Guid ProgrammeId, string ProgrammeCode, string ProgrammeNameEn, string ProgrammeNameAr,
    int Active, int AtRisk, int Withdrawn, int Completed)
{
    public int Enrolled => Active + AtRisk + Completed;

    public int Total => Active + AtRisk + Withdrawn + Completed;

    /// <summary>Active and At-Risk enrolments as a share of capacity — the seat occupancy of a live section.</summary>
    public decimal OccupancyPercent => Capacity == 0 ? 0 : Math.Round(100m * (Active + AtRisk) / Capacity, 1);
}

public sealed record ParticipationReport(ReportFilter Filter, IReadOnlyList<ParticipationRow> Rows, int Sections, int Active, int AtRisk, int Withdrawn, int Completed);

/// <summary>Attainment per section with at least one completed enrolment (BO-04): outcomes against the configured pass threshold.</summary>
public sealed record AttainmentRow(
    Guid SectionId, string SectionCode, string TermName,
    Guid CourseId, string CourseCode, string CourseNameEn, string CourseNameAr,
    Guid ProgrammeId, string ProgrammeCode, string ProgrammeNameEn, string ProgrammeNameAr,
    int Completed, int Passed, decimal AverageFinalGrade, decimal HighestFinalGrade, decimal LowestFinalGrade, int CertificatesIssued)
{
    public decimal PassRatePercent => Completed == 0 ? 0 : Math.Round(100m * Passed / Completed, 1);
}

public sealed record AttainmentReport(ReportFilter Filter, decimal PassThresholdPercent, IReadOnlyList<AttainmentRow> Rows, int Sections, int Completed, int Passed, int CertificatesIssued)
{
    public decimal PassRatePercent => Completed == 0 ? 0 : Math.Round(100m * Passed / Completed, 1);
}

/// <summary>Read-only aggregate queries behind the reports, declared here and answered by infrastructure in one round trip each.</summary>
public interface IReportRepository
{
    Task<IReadOnlyList<ParticipationRow>> ParticipationAsync(ReportFilter filter, CancellationToken cancellationToken);

    Task<IReadOnlyList<AttainmentRow>> AttainmentAsync(ReportFilter filter, decimal passThresholdPercent, CancellationToken cancellationToken);
}

/// <summary>
/// Operational reporting on participation and attainment (BO-04; Appendix C `sis.report.read`). Reports are computed
/// from the live records on request (no snapshot), ordered by programme, course, term and section so the rows read as
/// the catalogue does. The pass threshold is the Appendix B "Academic" value BR-10 uses, so "passed" here means
/// "eligible for a certificate".
/// </summary>
public sealed class ReportingService(IReportRepository reports, IOptions<AcademicOptions> academic)
{
    public async Task<ParticipationReport> ParticipationAsync(ReportFilter filter, CancellationToken cancellationToken)
    {
        var rows = await reports.ParticipationAsync(filter, cancellationToken);
        return new ParticipationReport(filter, rows, rows.Count, rows.Sum(r => r.Active), rows.Sum(r => r.AtRisk), rows.Sum(r => r.Withdrawn), rows.Sum(r => r.Completed));
    }

    public async Task<AttainmentReport> AttainmentAsync(ReportFilter filter, CancellationToken cancellationToken)
    {
        var threshold = academic.Value.PassThresholdPercent;
        var rows = await reports.AttainmentAsync(filter, threshold, cancellationToken);
        return new AttainmentReport(filter, threshold, rows, rows.Count, rows.Sum(r => r.Completed), rows.Sum(r => r.Passed), rows.Sum(r => r.CertificatesIssued));
    }
}
