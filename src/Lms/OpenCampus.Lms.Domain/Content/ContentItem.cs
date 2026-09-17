using OpenCampus.SharedKernel;

namespace OpenCampus.Lms.Domain.Content;

/// <summary>
/// One deliverable unit within a course content section (SDD 13.4: CourseContent 1—* ContentItem).
/// Part of the course content aggregate; owns its resources. Enforces BR-15: an unpublished item is
/// not visible to a learner.
/// </summary>
public sealed class ContentItem : Entity
{
    public const int TitleMaxLength = 200;
    public const int PageBodyMaxLength = 20000;
    public const int LinkBodyMaxLength = 2000;
    public const int DescriptionMaxLength = 2000;

    private readonly List<Resource> _resources = [];

    private ContentItem()
    {
    }

    public Guid CourseContentId { get; private set; }

    public string TitleEn { get; private set; } = null!;

    public string TitleAr { get; private set; } = null!;

    public ContentItemType ItemType { get; private set; }

    /// <summary>Page text, link address or file description according to <see cref="ItemType"/>.</summary>
    public string? Body { get; private set; }

    public int SortOrder { get; private set; }

    public bool IsPublished { get; private set; }

    public IReadOnlyCollection<Resource> Resources => _resources.AsReadOnly();

    /// <summary>BR-15 as a predicate, for building the learner's view of a content section.</summary>
    public bool IsVisibleToLearners => IsPublished;

    internal static ContentItem Create(Guid courseContentId, string titleEn, string titleAr, ContentItemType itemType, string? body, int sortOrder)
    {
        if (!Enum.IsDefined(itemType))
        {
            throw new DomainException("itemType is not a recognised content item type.");
        }

        var item = new ContentItem
        {
            CourseContentId = Guard.RequireId(courseContentId, nameof(courseContentId)),
            ItemType = itemType,
            IsPublished = false,
        };
        item.Amend(titleEn, titleAr, body, sortOrder);
        return item;
    }

    /// <summary>The type is fixed at creation because the meaning of the body and of attached resources depends on it.</summary>
    internal void Amend(string titleEn, string titleAr, string? body, int sortOrder)
    {
        TitleEn = Guard.RequireText(titleEn, nameof(titleEn), TitleMaxLength);
        TitleAr = Guard.RequireText(titleAr, nameof(titleAr), TitleMaxLength);
        SortOrder = sortOrder < 0 ? throw new DomainException("sortOrder must not be negative.") : sortOrder;
        Body = ItemType switch
        {
            ContentItemType.Page => Guard.RequireText(body ?? string.Empty, nameof(body), PageBodyMaxLength),
            ContentItemType.Link => RequireAbsoluteHttpAddress(body),
            _ => Guard.OptionalText(body, nameof(body), DescriptionMaxLength),
        };
    }

    internal void Reorder(int sortOrder)
    {
        SortOrder = sortOrder < 0 ? throw new DomainException("sortOrder must not be negative.") : sortOrder;
    }

    /// <summary>Publication (15.3 "content item management and publication"); a file item needs at least one resource to publish.</summary>
    internal void Publish()
    {
        if (ItemType == ContentItemType.File && _resources.Count == 0)
        {
            throw new DomainException("A file item cannot be published before a resource is attached.");
        }

        IsPublished = true;
    }

    internal void Unpublish()
    {
        IsPublished = false;
    }

    /// <summary>BR-15: a learner may not access an unpublished item.</summary>
    public void EnsureVisibleToLearner()
    {
        if (!IsVisibleToLearners)
        {
            throw BusinessRules.Br15ContentNotPublished();
        }
    }

    internal Resource AttachResource(string fileName, string storedPath, string contentType, long sizeBytes, string contentHash)
    {
        if (_resources.Any(r => string.Equals(r.StoredPath, storedPath, StringComparison.OrdinalIgnoreCase)))
        {
            throw new DomainException("A resource with the same stored path is already attached.");
        }

        var resource = Resource.Create(Id, fileName, storedPath, contentType, sizeBytes, contentHash);
        _resources.Add(resource);
        return resource;
    }

    internal Resource DetachResource(Guid resourceId)
    {
        var resource = FindResource(resourceId);
        _resources.Remove(resource);
        return resource;
    }

    public Resource? FindResourceOrDefault(Guid resourceId) => _resources.SingleOrDefault(r => r.Id == resourceId);

    private Resource FindResource(Guid resourceId) =>
        FindResourceOrDefault(resourceId) ?? throw new EntityNotFoundException(nameof(Resource), resourceId);

    private static string RequireAbsoluteHttpAddress(string? body)
    {
        var text = Guard.RequireText(body ?? string.Empty, nameof(body), LinkBodyMaxLength);
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new DomainException("A link item's body must be an absolute http or https address.");
        }

        return text;
    }
}
