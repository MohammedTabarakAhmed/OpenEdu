using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using OpenCampus.Lms.Application.Storage;

namespace OpenCampus.Lms.Infrastructure.Storage;

/// <summary>
/// SDD 18.5: a configured root directory with a deterministic hierarchical layout derived from the owning
/// entity identifiers (the caller supplies that layout as the relative directory). Every stored name is
/// generated here (SEC-23); the SHA-256 digest is computed while writing (SEC-26); writing is abandoned
/// the moment the configured size limit is exceeded (SEC-22). The root is never registered with static
/// file middleware (SEC-24) — retrieval happens only through <see cref="OpenReadAsync"/> behind an
/// authorised endpoint (SEC-25).
/// </summary>
public sealed class LocalFileStore : IFileStore
{
    private readonly string _root;

    public LocalFileStore(IOptions<StorageOptions> options, string contentRootPath)
    {
        var configured = options.Value.RootPath;
        _root = Path.GetFullPath(Path.IsPathRooted(configured) ? configured : Path.Combine(contentRootPath, configured));
    }

    /// <summary>The absolute root, exposed for start-up checks and tests only.</summary>
    public string RootPath => _root;

    public async Task<StoredFile?> SaveAsync(string relativeDirectory, string extension, Stream content, long maxSizeBytes, CancellationToken cancellationToken)
    {
        var directory = Resolve(relativeDirectory);
        Directory.CreateDirectory(directory);

        // SEC-23: the stored name is a fresh identifier plus the validated extension; no client input reaches the path.
        var storedName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        var absolutePath = Path.Combine(directory, storedName);
        var relativePath = Path.Combine(relativeDirectory, storedName).Replace('\\', '/');

        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long written = 0;

        try
        {
            await using (var target = new FileStream(absolutePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    written += read;
                    if (written > maxSizeBytes)
                    {
                        // SEC-22 at application level: stop reading; the partial file is removed below.
                        return null;
                    }

                    hasher.AppendData(buffer, 0, read);
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
            }

            if (written == 0)
            {
                return null;
            }

            return new StoredFile(relativePath, written, Convert.ToHexStringLower(hasher.GetHashAndReset()));
        }
        finally
        {
            if (written == 0 || written > maxSizeBytes)
            {
                File.Delete(absolutePath);
            }
        }
    }

    public Task<Stream?> OpenReadAsync(string relativePath, CancellationToken cancellationToken)
    {
        var absolutePath = Resolve(relativePath);
        Stream? stream = File.Exists(absolutePath)
            ? new FileStream(absolutePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true)
            : null;
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string relativePath, CancellationToken cancellationToken)
    {
        var absolutePath = Resolve(relativePath);
        if (File.Exists(absolutePath))
        {
            File.Delete(absolutePath);
        }

        return Task.CompletedTask;
    }

    /// <summary>Maps a relative path to the store and refuses anything that would escape the root.</summary>
    private string Resolve(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            throw new ArgumentException("A relative path within the file store is required.", nameof(relativePath));
        }

        var absolute = Path.GetFullPath(Path.Combine(_root, relativePath));
        if (!absolute.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The path resolves outside the file store.", nameof(relativePath));
        }

        return absolute;
    }
}
