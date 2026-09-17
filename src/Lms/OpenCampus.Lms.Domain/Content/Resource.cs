using OpenCampus.SharedKernel;

namespace OpenCampus.Lms.Domain.Content;

/// <summary>
/// A stored file attached to a content item (SDD 13.4: ContentItem 1—* Resource). Part of the course
/// content aggregate. The stored path is system-generated and relative to the configured store (SEC-23,
/// 18.5); the client-supplied name is a display attribute only; the hash permits integrity checks (SEC-26).
/// </summary>
public sealed class Resource : Entity
{
    public const int FileNameMaxLength = 255;
    public const int StoredPathMaxLength = 400;
    public const int ContentTypeMaxLength = 127;
    public const int ContentHashMaxLength = 64;

    private Resource()
    {
    }

    public Guid ContentItemId { get; private set; }

    /// <summary>The name supplied by the uploader, retained for display and download only (SEC-23).</summary>
    public string FileName { get; private set; } = null!;

    /// <summary>Relative path within the file store; never a client-supplied value (SEC-23, 18.5).</summary>
    public string StoredPath { get; private set; } = null!;

    public string ContentType { get; private set; } = null!;

    public long SizeBytes { get; private set; }

    /// <summary>Lower-case hexadecimal SHA-256 of the stored bytes (SEC-26).</summary>
    public string ContentHash { get; private set; } = null!;

    internal static Resource Create(Guid contentItemId, string fileName, string storedPath, string contentType, long sizeBytes, string contentHash)
    {
        if (sizeBytes <= 0)
        {
            throw new DomainException("sizeBytes must be greater than zero.");
        }

        var hash = Guard.RequireText(contentHash, nameof(contentHash), ContentHashMaxLength).ToLowerInvariant();
        if (hash.Length != ContentHashMaxLength || !hash.All(Uri.IsHexDigit))
        {
            throw new DomainException("contentHash must be a SHA-256 digest in hexadecimal form.");
        }

        var path = Guard.RequireText(storedPath, nameof(storedPath), StoredPathMaxLength);
        if (Path.IsPathRooted(path) || path.Contains("..", StringComparison.Ordinal))
        {
            throw new DomainException("storedPath must be a relative path within the file store.");
        }

        return new Resource
        {
            ContentItemId = Guard.RequireId(contentItemId, nameof(contentItemId)),
            FileName = Guard.RequireText(Path.GetFileName(fileName), nameof(fileName), FileNameMaxLength),
            StoredPath = path,
            ContentType = Guard.RequireText(contentType, nameof(contentType), ContentTypeMaxLength),
            SizeBytes = sizeBytes,
            ContentHash = hash,
        };
    }
}
