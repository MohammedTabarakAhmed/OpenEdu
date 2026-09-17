namespace OpenCampus.Lms.Application.Storage;

/// <summary>Outcome of a store operation: the system-generated relative path, the byte count and the SHA-256 hex digest (SEC-23, SEC-26).</summary>
public sealed record StoredFile(string RelativePath, long SizeBytes, string ContentHash);

/// <summary>
/// The file storage abstraction of SDD 18.5, declared in the application layer so the physical mechanism can
/// be replaced without change to consuming code. Implementations generate every stored name (SEC-23), lay
/// files out deterministically beneath a configured root (18.5) and never serve them statically (SEC-24).
/// </summary>
public interface IFileStore
{
    /// <summary>
    /// Writes the content beneath <paramref name="relativeDirectory"/> (derived by the caller from the owning
    /// entity identifiers) under a generated name carrying <paramref name="extension"/>. Writing stops and
    /// nothing is kept if the content exceeds <paramref name="maxSizeBytes"/> (SEC-22, application level).
    /// </summary>
    Task<StoredFile?> SaveAsync(string relativeDirectory, string extension, Stream content, long maxSizeBytes, CancellationToken cancellationToken);

    /// <summary>A readable stream for a stored file, or null when it is absent.</summary>
    Task<Stream?> OpenReadAsync(string relativePath, CancellationToken cancellationToken);

    /// <summary>Removes a stored file; absence is not an error.</summary>
    Task DeleteAsync(string relativePath, CancellationToken cancellationToken);
}
