using Microsoft.EntityFrameworkCore;
using OpenCampus.Sis.Application.Reports;
using OpenCampus.Sis.Domain.Enrolments;
using OpenCampus.Sis.Domain.Sections;

namespace OpenCampus.Sis.Infrastructure.Persistence.Repositories;

/// <summary>
/// Report aggregates computed in the database (NFR-03/04: one grouped query per report, no per-row round trips).
/// The section → course → programme join is the catalogue's own; the logical-deletion filter applies throughout.
/// </summary>
internal sealed class ReportRepository(SisDbContext db) : IReportRepository
{
    /// <summary>The filtered section → course → programme join, projected once the filters are applied (EF translates filters on entities, not on a projected record).</summary>
    private IQueryable<SectionShape> Sections(ReportFilter filter)
    {
        var joined = db.Sections
            .Join(db.Courses, s => s.CourseId, c => c.Id, (s, c) => new { Section = s, Course = c })
            .Join(db.Programmes, sc => sc.Course.ProgrammeId, p => p.Id, (sc, p) => new { sc.Section, sc.Course, Programme = p });

        if (filter.ProgrammeId is { } programmeId)
        {
            joined = joined.Where(x => x.Programme.Id == programmeId);
        }

        if (filter.CourseId is { } courseId)
        {
            joined = joined.Where(x => x.Course.Id == courseId);
        }

        if (!string.IsNullOrWhiteSpace(filter.TermName))
        {
            var term = filter.TermName.Trim();
            joined = joined.Where(x => x.Section.TermName == term);
        }

        if (filter.Status is { } status)
        {
            joined = joined.Where(x => x.Section.Status == status);
        }

        return joined.Select(x => new SectionShape(
            x.Section.Id, x.Section.Code, x.Section.TermName, x.Section.Status, x.Section.Capacity,
            x.Course.Id, x.Course.Code, x.Course.NameEn, x.Course.NameAr,
            x.Programme.Id, x.Programme.Code, x.Programme.NameEn, x.Programme.NameAr));
    }

    public async Task<IReadOnlyList<ParticipationRow>> ParticipationAsync(ReportFilter filter, CancellationToken cancellationToken)
    {
        var sections = await Sections(filter).ToListAsync(cancellationToken);
        var ids = sections.Select(s => s.SectionId).ToArray();

        var counts = await db.Enrolments
            .Where(e => ids.Contains(e.SectionId))
            .GroupBy(e => new { e.SectionId, e.Status })
            .Select(g => new { g.Key.SectionId, g.Key.Status, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var bySection = counts.ToLookup(c => c.SectionId);

        return sections
            .OrderBy(s => s.ProgrammeCode).ThenBy(s => s.CourseCode).ThenBy(s => s.TermName).ThenBy(s => s.SectionCode)
            .Select(s =>
            {
                int Of(EnrolmentStatus status) => bySection[s.SectionId].Where(c => c.Status == status).Sum(c => c.Count);
                return new ParticipationRow(
                    s.SectionId, s.SectionCode, s.TermName, s.SectionStatus, s.Capacity,
                    s.CourseId, s.CourseCode, s.CourseNameEn, s.CourseNameAr,
                    s.ProgrammeId, s.ProgrammeCode, s.ProgrammeNameEn, s.ProgrammeNameAr,
                    Of(EnrolmentStatus.Active), Of(EnrolmentStatus.AtRisk), Of(EnrolmentStatus.Withdrawn), Of(EnrolmentStatus.Completed));
            })
            .ToList();
    }

    public async Task<IReadOnlyList<AttainmentRow>> AttainmentAsync(ReportFilter filter, decimal passThresholdPercent, CancellationToken cancellationToken)
    {
        var sections = await Sections(filter).ToListAsync(cancellationToken);
        var ids = sections.Select(s => s.SectionId).ToArray();

        var outcomes = await db.Enrolments
            .Where(e => ids.Contains(e.SectionId) && e.Status == EnrolmentStatus.Completed && e.FinalGrade != null)
            .GroupBy(e => e.SectionId)
            .Select(g => new
            {
                SectionId = g.Key,
                Completed = g.Count(),
                Passed = g.Count(e => e.FinalGrade >= passThresholdPercent),
                Average = g.Average(e => e.FinalGrade!.Value),
                Highest = g.Max(e => e.FinalGrade!.Value),
                Lowest = g.Min(e => e.FinalGrade!.Value),
            })
            .ToListAsync(cancellationToken);

        var certificates = await db.Certificates
            .Join(db.Enrolments.Where(e => ids.Contains(e.SectionId)), c => c.EnrolmentId, e => e.Id, (c, e) => e.SectionId)
            .GroupBy(sectionId => sectionId)
            .Select(g => new { SectionId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.SectionId, x => x.Count, cancellationToken);

        var bySection = sections.ToDictionary(s => s.SectionId);
        return outcomes
            .Select(o =>
            {
                var s = bySection[o.SectionId];
                return new AttainmentRow(
                    s.SectionId, s.SectionCode, s.TermName,
                    s.CourseId, s.CourseCode, s.CourseNameEn, s.CourseNameAr,
                    s.ProgrammeId, s.ProgrammeCode, s.ProgrammeNameEn, s.ProgrammeNameAr,
                    o.Completed, o.Passed, Math.Round(o.Average, 2), o.Highest, o.Lowest, certificates.GetValueOrDefault(o.SectionId));
            })
            .OrderBy(r => r.ProgrammeCode).ThenBy(r => r.CourseCode).ThenBy(r => r.TermName).ThenBy(r => r.SectionCode)
            .ToList();
    }

    private sealed record SectionShape(
        Guid SectionId, string SectionCode, string TermName, SectionStatus SectionStatus, int Capacity,
        Guid CourseId, string CourseCode, string CourseNameEn, string CourseNameAr,
        Guid ProgrammeId, string ProgrammeCode, string ProgrammeNameEn, string ProgrammeNameAr);
}
