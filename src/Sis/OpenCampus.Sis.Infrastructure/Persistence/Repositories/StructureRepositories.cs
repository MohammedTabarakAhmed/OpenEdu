using Microsoft.EntityFrameworkCore;
using OpenCampus.SharedKernel;
using OpenCampus.Sis.Application.Abstractions;
using OpenCampus.Sis.Domain.Courses;
using OpenCampus.Sis.Domain.Programmes;
using OpenCampus.Sis.Domain.Sections;

namespace OpenCampus.Sis.Infrastructure.Persistence.Repositories;

internal sealed class ProgrammeRepository(SisDbContext db) : IProgrammeRepository
{
    public Task<Programme?> FindByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.Programmes.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, Programme>> FindManyAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        var set = ids.Distinct().ToArray();
        return await db.Programmes.Where(p => set.Contains(p.Id)).ToDictionaryAsync(p => p.Id, cancellationToken);
    }

    public Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken) =>
        db.Programmes.AnyAsync(p => p.Code == code, cancellationToken);

    public Task<int> CountCoursesAsync(Guid programmeId, CancellationToken cancellationToken) =>
        db.Courses.CountAsync(c => c.ProgrammeId == programmeId, cancellationToken);

    public async Task<PagedResponse<Programme>> ListAsync(ProgrammeQuery query, CancellationToken cancellationToken)
    {
        IQueryable<Programme> source = db.Programmes;

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            source = source.Where(p => p.Code.Contains(term) || p.NameEn.Contains(term) || p.NameAr.Contains(term));
        }

        if (query.IsActive is { } isActive)
        {
            source = source.Where(p => p.IsActive == isActive);
        }

        source = (query.Sort, query.Descending) switch
        {
            ("nameen", false) => source.OrderBy(p => p.NameEn),
            ("nameen", true) => source.OrderByDescending(p => p.NameEn),
            ("durationmonths", false) => source.OrderBy(p => p.DurationMonths),
            ("durationmonths", true) => source.OrderByDescending(p => p.DurationMonths),
            ("createdatutc", false) => source.OrderBy(p => p.CreatedAtUtc),
            ("createdatutc", true) => source.OrderByDescending(p => p.CreatedAtUtc),
            (_, true) => source.OrderByDescending(p => p.Code),
            _ => source.OrderBy(p => p.Code),
        };

        return await source.ToPageAsync(query.Page, query.PageSize, cancellationToken);
    }

    public void Add(Programme programme) => db.Programmes.Add(programme);
}

internal sealed class CourseRepository(SisDbContext db) : ICourseRepository
{
    public Task<Course?> FindByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.Courses.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, Course>> FindManyAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        var set = ids.Distinct().ToArray();
        return await db.Courses.Where(c => set.Contains(c.Id)).ToDictionaryAsync(c => c.Id, cancellationToken);
    }

    public Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken) =>
        db.Courses.AnyAsync(c => c.Code == code, cancellationToken);

    public Task<int> CountSectionsAsync(Guid courseId, CancellationToken cancellationToken) =>
        db.Sections.CountAsync(s => s.CourseId == courseId, cancellationToken);

    public async Task<PagedResponse<Course>> ListAsync(CourseQuery query, CancellationToken cancellationToken)
    {
        IQueryable<Course> source = db.Courses;

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            source = source.Where(c => c.Code.Contains(term) || c.NameEn.Contains(term) || c.NameAr.Contains(term));
        }

        if (query.ProgrammeId is { } programmeId)
        {
            source = source.Where(c => c.ProgrammeId == programmeId);
        }

        source = (query.Sort, query.Descending) switch
        {
            ("nameen", false) => source.OrderBy(c => c.NameEn),
            ("nameen", true) => source.OrderByDescending(c => c.NameEn),
            ("credits", false) => source.OrderBy(c => c.Credits).ThenBy(c => c.Code),
            ("credits", true) => source.OrderByDescending(c => c.Credits).ThenBy(c => c.Code),
            ("createdatutc", false) => source.OrderBy(c => c.CreatedAtUtc),
            ("createdatutc", true) => source.OrderByDescending(c => c.CreatedAtUtc),
            (_, true) => source.OrderByDescending(c => c.Code),
            _ => source.OrderBy(c => c.Code),
        };

        return await source.ToPageAsync(query.Page, query.PageSize, cancellationToken);
    }

    public void Add(Course course) => db.Courses.Add(course);
}

internal sealed class SectionRepository(SisDbContext db) : ISectionRepository
{
    public Task<CourseSection?> FindByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.Sections
            .Include(s => s.Sessions)
            .Include(s => s.GradeComponents)
            .AsSplitQuery()
            .SingleOrDefaultAsync(s => s.Id == id, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, CourseSection>> FindManyAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        var set = ids.Distinct().ToArray();
        return await db.Sections.Where(s => set.Contains(s.Id)).ToDictionaryAsync(s => s.Id, cancellationToken);
    }

    public Task<bool> CodeExistsAsync(Guid courseId, string code, CancellationToken cancellationToken) =>
        db.Sections.AnyAsync(s => s.CourseId == courseId && s.Code == code, cancellationToken);

    public async Task<IReadOnlyList<Session>> GetInstructorSessionsElsewhereAsync(Guid instructorUserId, Guid excludingSectionId, CancellationToken cancellationToken)
    {
        // One query: the instructor's other live sections joined to their sessions (BR-16 input).
        var liveStatuses = new[] { SectionStatus.Draft, SectionStatus.Open };
        return await db.Sessions
            .Join(db.Sections, x => x.SectionId, s => s.Id, (session, section) => new { session, section })
            .Where(j => j.section.InstructorUserId == instructorUserId && j.section.Id != excludingSectionId && liveStatuses.Contains(j.section.Status))
            .Select(j => j.session)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public async Task<PagedResponse<CourseSection>> ListAsync(SectionQuery query, CancellationToken cancellationToken)
    {
        IQueryable<CourseSection> source = db.Sections;

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            var matchingCourses = db.Courses.Where(c => c.Code.Contains(term) || c.NameEn.Contains(term) || c.NameAr.Contains(term)).Select(c => c.Id);
            source = source.Where(s => s.Code.Contains(term) || s.TermName.Contains(term) || matchingCourses.Contains(s.CourseId));
        }

        if (query.CourseId is { } courseId)
        {
            source = source.Where(s => s.CourseId == courseId);
        }

        if (query.ProgrammeId is { } programmeId)
        {
            var programmeCourses = db.Courses.Where(c => c.ProgrammeId == programmeId).Select(c => c.Id);
            source = source.Where(s => programmeCourses.Contains(s.CourseId));
        }

        if (query.Status is { } status)
        {
            source = source.Where(s => s.Status == status);
        }

        if (query.DeliveryMode is { } mode)
        {
            source = source.Where(s => s.DeliveryMode == mode);
        }

        if (query.InstructorUserId is { } instructor)
        {
            source = source.Where(s => s.InstructorUserId == instructor);
        }

        source = (query.Sort, query.Descending) switch
        {
            ("termname", false) => source.OrderBy(s => s.TermName).ThenBy(s => s.Code),
            ("termname", true) => source.OrderByDescending(s => s.TermName).ThenBy(s => s.Code),
            ("startdate", false) => source.OrderBy(s => s.StartDate).ThenBy(s => s.Code),
            ("startdate", true) => source.OrderByDescending(s => s.StartDate).ThenBy(s => s.Code),
            ("status", false) => source.OrderBy(s => s.Status).ThenBy(s => s.Code),
            ("status", true) => source.OrderByDescending(s => s.Status).ThenBy(s => s.Code),
            ("createdatutc", false) => source.OrderBy(s => s.CreatedAtUtc),
            ("createdatutc", true) => source.OrderByDescending(s => s.CreatedAtUtc),
            (_, true) => source.OrderByDescending(s => s.Code),
            _ => source.OrderBy(s => s.Code),
        };

        return await source.ToPageAsync(query.Page, query.PageSize, cancellationToken);
    }

    public void Add(CourseSection section) => db.Sections.Add(section);
}

/// <summary>Server-side paging shared by every collection query (NFR-03): one count, one bounded page.</summary>
internal static class PagingExtensions
{
    public static async Task<PagedResponse<T>> ToPageAsync<T>(this IQueryable<T> source, int page, int pageSize, CancellationToken cancellationToken)
    {
        var total = await source.CountAsync(cancellationToken);
        var items = await source.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return new PagedResponse<T>(items, page, pageSize, total);
    }
}
