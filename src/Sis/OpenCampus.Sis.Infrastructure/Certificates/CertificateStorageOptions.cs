namespace OpenCampus.Sis.Infrastructure.Certificates;

/// <summary>
/// The SIS view of Appendix B "Storage": only the root path is needed for certificate documents. Bound from the same
/// "Storage" section the LMS uses so one configured root (18.5) serves every module, each beneath its own sub-tree.
/// </summary>
public sealed class CertificateStorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>Root directory of the file store, relative to the host content root or absolute. Never served statically (SEC-24).</summary>
    public string RootPath { get; set; } = string.Empty;

    public bool Validate() => !string.IsNullOrWhiteSpace(RootPath);
}
