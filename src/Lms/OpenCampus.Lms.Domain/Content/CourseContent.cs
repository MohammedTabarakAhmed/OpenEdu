using OpenCampus.SharedKernel;

namespace OpenCampus.Lms.Domain.Content;

/// <summary>
/// Aggregate root for one unit of a section's content hierarchy (SDD 13.4: "CourseContent" — a titled,
/// ordered grouping such as a week or a topic). Owns its content items and, through them, their
/// resources. SectionId is a cross-module reference to the SIS section, held as an identifier only (MB-03);
/// the section's existence and the caller's entitlement to it are established through the LMS→SIS
/// contract (6.5, 13.5), never here.
/// </summary>
public sealed class CourseContent : Entity
{
    public const int TitleMaxLength = 200;

    private readonly List<ContentItem> _items = [];

    private CourseContent()
    {
    }

    public Guid SectionId { get; private set; }

    public string TitleEn { get; private set; } = null!;

    public string TitleAr { get; private set; } = null!;

    public int SortOrder { get; private set; }

    public IReadOnlyCollection<ContentItem> Items => _items.AsReadOnly();

    /// <summary>BR-15: the learner's view contains published items only.</summary>
    public IEnumerable<ContentItem> ItemsVisibleToLearners => _items.Where(i => i.IsVisibleToLearners);

    public static CourseContent Create(Guid sectionId, string titleEn, string titleAr, int sortOrder)
    {
        var content = new CourseContent { SectionId = Guard.RequireId(sectionId, nameof(sectionId)) };
        content.Amend(titleEn, titleAr, sortOrder);
        return content;
    }

    public void Amend(string titleEn, string titleAr, int sortOrder)
    {
        TitleEn = Guard.RequireText(titleEn, nameof(titleEn), TitleMaxLength);
        TitleAr = Guard.RequireText(titleAr, nameof(titleAr), TitleMaxLength);
        SortOrder = sortOrder < 0 ? throw new DomainException("sortOrder must not be negative.") : sortOrder;
    }

    // ----- Items (15.3 "content item management and publication") -----

    public ContentItem AddItem(string titleEn, string titleAr, ContentItemType itemType, string? body, int? sortOrder = null)
    {
        var item = ContentItem.Create(Id, titleEn, titleAr, itemType, body, sortOrder ?? NextSortOrder());
        _items.Add(item);
        return item;
    }

    public ContentItem AmendItem(Guid itemId, string titleEn, string titleAr, string? body, int sortOrder)
    {
        var item = FindItem(itemId);
        item.Amend(titleEn, titleAr, body, sortOrder);
        return item;
    }

    /// <summary>Removing an item returns its resources so the caller can delete the stored files after the commit.</summary>
    public IReadOnlyList<Resource> RemoveItem(Guid itemId)
    {
        var item = FindItem(itemId);
        _items.Remove(item);
        return item.Resources.ToList();
    }

    public ContentItem PublishItem(Guid itemId)
    {
        var item = FindItem(itemId);
        item.Publish();
        return item;
    }

    public ContentItem UnpublishItem(Guid itemId)
    {
        var item = FindItem(itemId);
        item.Unpublish();
        return item;
    }

    /// <summary>Applies a complete new ordering; every item of the section must be named exactly once.</summary>
    public void ReorderItems(IReadOnlyList<Guid> orderedItemIds)
    {
        if (orderedItemIds.Count != _items.Count || orderedItemIds.Distinct().Count() != _items.Count || _items.Any(i => !orderedItemIds.Contains(i.Id)))
        {
            throw new DomainException("The ordering must name every item of this content exactly once.");
        }

        for (var position = 0; position < orderedItemIds.Count; position++)
        {
            FindItem(orderedItemIds[position]).Reorder(position);
        }
    }

    // ----- Resources (15.3 "resource upload") -----

    public Resource AttachResource(Guid itemId, string fileName, string storedPath, string contentType, long sizeBytes, string contentHash) =>
        FindItem(itemId).AttachResource(fileName, storedPath, contentType, sizeBytes, contentHash);

    public Resource DetachResource(Guid itemId, Guid resourceId) => FindItem(itemId).DetachResource(resourceId);

    /// <summary>All resources of the aggregate, for deleting stored files when the content is removed.</summary>
    public IEnumerable<Resource> AllResources => _items.SelectMany(i => i.Resources);

    public ContentItem? FindItemOrDefault(Guid itemId) => _items.SingleOrDefault(i => i.Id == itemId);

    /// <summary>The item as a learner may see it: null when absent or unpublished (BR-15, reported as not found per API-06).</summary>
    public ContentItem? FindItemForLearner(Guid itemId)
    {
        var item = FindItemOrDefault(itemId);
        return item is { IsVisibleToLearners: true } ? item : null;
    }

    private ContentItem FindItem(Guid itemId) =>
        FindItemOrDefault(itemId) ?? throw new EntityNotFoundException(nameof(ContentItem), itemId);

    private int NextSortOrder() => _items.Count == 0 ? 0 : _items.Max(i => i.SortOrder) + 1;
}
