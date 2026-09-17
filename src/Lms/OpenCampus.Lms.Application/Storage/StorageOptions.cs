namespace OpenCampus.Lms.Application.Storage;

/// <summary>File storage configuration (SDD Appendix B, "Storage"; SEC-22, SEC-24, 18.5). Bound from the "Storage" section.</summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>Root directory of the file store, relative to the host content root or absolute. Must not be a statically served directory (SEC-24).</summary>
    public string RootPath { get; set; } = string.Empty;

    /// <summary>Upper bound for one uploaded file, enforced at both framework and application level (SEC-22).</summary>
    public long MaxUploadSizeBytes { get; set; } = 25 * 1024 * 1024;

    /// <summary>Allow-list of file extensions including the leading dot, compared case-insensitively (SEC-22).</summary>
    public string[] PermittedExtensions { get; set; } = [];

    public bool Validate() =>
        !string.IsNullOrWhiteSpace(RootPath)
        && MaxUploadSizeBytes > 0
        && PermittedExtensions.Length > 0
        && PermittedExtensions.All(e => e.Length > 1 && e[0] == '.' && e.IndexOfAny(['/', '\\', '.'], 1) < 0);

    public bool IsPermittedExtension(string extension) =>
        PermittedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
}
