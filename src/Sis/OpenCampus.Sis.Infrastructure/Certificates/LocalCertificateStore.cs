using Microsoft.Extensions.Options;
using OpenCampus.Sis.Application.Certificates;

namespace OpenCampus.Sis.Infrastructure.Certificates;

/// <summary>
/// SDD 18.5 for certificate documents: a configured root with a deterministic layout supplied by the caller from the
/// owning identifiers, a generated file name (SEC-23) and retrieval only through <see cref="OpenReadAsync"/> behind
/// an authorised endpoint (SEC-24/25). Paths that would escape the root are refused.
/// </summary>
public sealed class LocalCertificateStore : ICertificateStore
{
    private readonly string _root;

    public LocalCertificateStore(IOptions<CertificateStorageOptions> options, string contentRootPath)
    {
        var configured = options.Value.RootPath;
        _root = Path.GetFullPath(Path.IsPathRooted(configured) ? configured : Path.Combine(contentRootPath, configured));
    }

    /// <summary>The absolute root, exposed for start-up checks and tests only.</summary>
    public string RootPath => _root;

    public async Task<string> SaveAsync(string relativeDirectory, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        var directory = Resolve(relativeDirectory);
        Directory.CreateDirectory(directory);

        var storedName = $"{Guid.NewGuid():N}.pdf";
        var absolutePath = Path.Combine(directory, storedName);
        await using (var target = new FileStream(absolutePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            await target.WriteAsync(content, cancellationToken);
        }

        return Path.Combine(relativeDirectory, storedName).Replace('\\', '/');
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
