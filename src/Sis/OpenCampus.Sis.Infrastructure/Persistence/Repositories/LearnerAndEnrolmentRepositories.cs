using Microsoft.EntityFrameworkCore;
using OpenCampus.SharedKernel;
using OpenCampus.Sis.Application.Abstractions;
using OpenCampus.Sis.Domain.Enrolments;
using OpenCampus.Sis.Domain.Learners;

namespace OpenCampus.Sis.Infrastructure.Persistence.Repositories;

internal sealed class LearnerRepository(SisDbContext db) : ILearnerRepository
{
    public Task<Learner?> FindByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.Learners.SingleOrDefaultAsync(l => l.Id == id, cancellationToken);

    public Task<Learner?> FindByUserIdAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Learners.SingleOrDefaultAsync(l => l.UserId == userId, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, Learner>> FindManyAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken)
    {
        var set = ids.Distinct().ToArray();
        return await db.Learners.Where(l => set.Contains(l.Id)).ToDictionaryAsync(l => l.Id, cancellationToken);
    }

    public Task<bool> LearnerNumberExistsAsync(string learnerNumber, CancellationToken cancellationToken) =>
        db.Learners.AnyAsync(l => l.LearnerNumber == learnerNumber, cancellationToken);

    public Task<bool> UserIdExistsAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Learners.AnyAsync(l => l.UserId == userId, cancellationToken);

    public async Task<IReadOnlySet<Guid>> GetLinkedUserIdsAsync(IEnumerable<Guid> userIds, CancellationToken cancellationToken)
    {
        var set = userIds.Distinct().ToArray();
        var linked = await db.Learners.Where(l => set.Contains(l.UserId)).Select(l => l.UserId).ToListAsync(cancellationToken);
        return linked.ToHashSet();
    }

    public async Task<PagedResponse<Learner>> ListAsync(LearnerQuery query, CancellationToken cancellationToken)
    {
        IQueryable<Learner> source = db.Learners;

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            var users = (query.MatchingUserIds ?? []).ToArray();
            source = source.Where(l => l.LearnerNumber.Contains(term) || users.Contains(l.UserId));
        }

        if (query.Status is { } status)
        {
            source = source.Where(l => l.Status == status);
        }

        source = (query.Sort, query.Descending) switch
        {
            ("status", false) => source.OrderBy(l => l.Status).ThenBy(l => l.LearnerNumber),
            ("status", true) => source.OrderByDescending(l => l.Status).ThenBy(l => l.LearnerNumber),
            ("createdatutc", false) => source.OrderBy(l => l.CreatedAtUtc),
            ("createdatutc", true) => source.OrderByDescending(l => l.CreatedAtUtc),
            (_, true) => source.OrderByDescending(l => l.LearnerNumber),
            _ => source.OrderBy(l => l.LearnerNumber),
        };

        return await source.ToPageAsync(query.Page, query.PageSize, cancellationToken);
    }

    public void Add(Learner learner) => db.Learners.Add(learner);
}

internal sealed class EnrolmentRepository(SisDbContext db) : IEnrolmentRepository
{
    private static readonly EnrolmentStatus[] ActiveStatuses = [EnrolmentStatus.Active, EnrolmentStatus.AtRisk];

    public Task<Enrolment?> FindByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.Enrolments.SingleOrDefaultAsync(e => e.Id == id, cancellationToken);

    public Task<Enrolment?> FindByLearnerAndSectionAsync(Guid learnerId, Guid sectionId, CancellationToken cancellationToken) =>
        db.Enrolments.SingleOrDefaultAsync(e => e.LearnerId == learnerId && e.SectionId == sectionId, cancellationToken);

    public Task<int> CountActiveInSectionAsync(Guid sectionId, CancellationToken cancellationToken) =>
        db.Enrolments.CountAsync(e => e.SectionId == sectionId && ActiveStatuses.Contains(e.Status), cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, int>> CountActiveInSectionsAsync(IEnumerable<Guid> sectionIds, CancellationToken cancellationToken)
    {
        var set = sectionIds.Distinct().ToArray();
        var counts = await db.Enrolments
            .Where(e => set.Contains(e.SectionId) && ActiveStatuses.Contains(e.Status))
            .GroupBy(e => e.SectionId)
            .Select(g => new { SectionId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        return counts.ToDictionary(c => c.SectionId, c => c.Count);
    }

    public Task<int> CountInSectionAsync(Guid sectionId, CancellationToken cancellationToken) =>
        db.Enrolments.CountAsync(e => e.SectionId == sectionId, cancellationToken);

    public async Task<PagedResponse<Enrolment>> ListAsync(EnrolmentQuery query, CancellationToken cancellationToken)
    {
        IQueryable<Enrolment> source = db.Enrolments;

        if (query.LearnerId is { } learnerId)
        {
            source = source.Where(e => e.LearnerId == learnerId);
        }

        if (query.SectionId is { } sectionId)
        {
            source = source.Where(e => e.SectionId == sectionId);
        }

        if (query.Status is { } status)
        {
            source = source.Where(e => e.Status == status);
        }

        if (query.ActiveOnly == true)
        {
            source = source.Where(e => ActiveStatuses.Contains(e.Status));
        }

        source = source.OrderByDescending(e => e.EnrolledAtUtc).ThenBy(e => e.Id);
        return await source.ToPageAsync(query.Page, query.PageSize, cancellationToken);
    }

    public async Task<IReadOnlyList<Enrolment>> ListActiveInSectionAsync(Guid sectionId, CancellationToken cancellationToken) =>
        await db.Enrolments
            .Where(e => e.SectionId == sectionId && (e.Status == EnrolmentStatus.Active || e.Status == EnrolmentStatus.AtRisk))
            .OrderBy(e => e.EnrolledAtUtc)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Enrolment>> ListGradableInSectionAsync(Guid sectionId, CancellationToken cancellationToken) =>
        await db.Enrolments
            .Where(e => e.SectionId == sectionId && e.Status != EnrolmentStatus.Withdrawn)
            .OrderBy(e => e.EnrolledAtUtc)
            .ToListAsync(cancellationToken);

    public Task<Enrolment?> FindByUserAndSectionAsync(Guid userId, Guid sectionId, CancellationToken cancellationToken) =>
        db.Enrolments
            .Where(e => e.SectionId == sectionId)
            .Join(db.Learners.Where(l => l.UserId == userId), e => e.LearnerId, l => l.Id, (e, _) => e)
            .SingleOrDefaultAsync(cancellationToken);

    public void Add(Enrolment enrolment) => db.Enrolments.Add(enrolment);
}

internal sealed class GradeEntryRepository(SisDbContext db) : IGradeEntryRepository
{
    public Task<GradeEntry?> FindByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.GradeEntries.SingleOrDefaultAsync(g => g.Id == id, cancellationToken);

    public Task<GradeEntry?> FindByEnrolmentAndComponentAsync(Guid enrolmentId, Guid gradeComponentId, CancellationToken cancellationToken) =>
        db.GradeEntries.SingleOrDefaultAsync(g => g.EnrolmentId == enrolmentId && g.GradeComponentId == gradeComponentId, cancellationToken);

    public async Task<IReadOnlyList<GradeEntry>> ListByEnrolmentAsync(Guid enrolmentId, CancellationToken cancellationToken) =>
        await db.GradeEntries.Where(g => g.EnrolmentId == enrolmentId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<GradeEntry>> ListBySectionAsync(Guid sectionId, CancellationToken cancellationToken) =>
        await db.GradeEntries
            .Join(db.Enrolments.Where(e => e.SectionId == sectionId), g => g.EnrolmentId, e => e.Id, (g, _) => g)
            .ToListAsync(cancellationToken);

    public void Add(GradeEntry entry) => db.GradeEntries.Add(entry);
}

internal sealed class SisUnitOfWork(SisDbContext db) : ISisUnitOfWork
{
    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
