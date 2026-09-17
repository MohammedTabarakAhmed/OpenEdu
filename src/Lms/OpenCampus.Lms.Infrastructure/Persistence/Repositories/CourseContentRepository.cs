using Microsoft.EntityFrameworkCore;
using OpenCampus.Lms.Application.Abstractions;
using OpenCampus.Lms.Domain.Content;

namespace OpenCampus.Lms.Infrastructure.Persistence.Repositories;

internal sealed class CourseContentRepository(LmsDbContext db) : ICourseContentRepository
{
    private IQueryable<CourseContent> Aggregates => db.CourseContents
        .Include(c => c.Items.OrderBy(i => i.SortOrder))
        .ThenInclude(i => i.Resources)
        .AsSplitQuery();

    public Task<CourseContent?> FindByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Aggregates.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task<CourseContent?> FindByResourceIdAsync(Guid resourceId, CancellationToken cancellationToken)
    {
        // Two bounded lookups rather than a join across the aggregate: resource → item → aggregate root.
        var contentId = await db.Resources
            .Where(r => r.Id == resourceId)
            .Join(db.ContentItems, r => r.ContentItemId, i => i.Id, (r, i) => i.CourseContentId)
            .SingleOrDefaultAsync(cancellationToken);

        return contentId == Guid.Empty ? null : await FindByIdAsync(contentId, cancellationToken);
    }

    public async Task<IReadOnlyList<CourseContent>> ListBySectionAsync(Guid sectionId, CancellationToken cancellationToken) =>
        await Aggregates
            .Where(c => c.SectionId == sectionId)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public Task<bool> AnyAsync(CancellationToken cancellationToken) => db.CourseContents.AnyAsync(cancellationToken);

    public void Add(CourseContent content) => db.CourseContents.Add(content);

    /// <summary>DC-03: logical deletion of the root and everything it owns; the query filter hides them thereafter.</summary>
    public void Remove(CourseContent content)
    {
        foreach (var item in content.Items)
        {
            foreach (var resource in item.Resources)
            {
                resource.MarkDeleted();
            }

            item.MarkDeleted();
        }

        content.MarkDeleted();
    }
}

internal sealed class LmsUnitOfWork(LmsDbContext db) : ILmsUnitOfWork
{
    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
