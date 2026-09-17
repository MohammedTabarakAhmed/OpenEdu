using System.Collections.Concurrent;
using PdfSharp.Fonts;

namespace OpenCampus.Sis.Infrastructure.Certificates;

/// <summary>
/// Supplies PDFsharp with font data from the operating system's font directories, so the renderer needs no bundled
/// font files. The certificate asks for a sans-serif and a monospace face; each is satisfied by the first family found
/// from a short candidate list covering Windows (Arial, Courier New) and common Linux distributions (DejaVu, Liberation).
/// </summary>
internal sealed class PlatformFontResolver : IFontResolver
{
    public const string Sans = "certificate-sans";
    public const string Mono = "certificate-mono";

    private static readonly IReadOnlyDictionary<string, string[][]> Candidates = new Dictionary<string, string[][]>
    {
        // Per family: [regular file names], [bold file names]; the first existing file wins.
        [Sans] = [["arial.ttf", "DejaVuSans.ttf", "LiberationSans-Regular.ttf"], ["arialbd.ttf", "DejaVuSans-Bold.ttf", "LiberationSans-Bold.ttf"]],
        [Mono] = [["cour.ttf", "DejaVuSansMono.ttf", "LiberationMono-Regular.ttf"], ["courbd.ttf", "DejaVuSansMono-Bold.ttf", "LiberationMono-Bold.ttf"]],
    };

    private static readonly string[] Directories = BuildDirectories();

    private readonly ConcurrentDictionary<string, byte[]?> _cache = new(StringComparer.OrdinalIgnoreCase);

    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic)
    {
        if (!Candidates.TryGetValue(familyName, out var byWeight))
        {
            return null;
        }

        // Fall back to the regular face if no bold file exists; italic is not used by the certificate.
        var path = Find(byWeight[bold ? 1 : 0]) ?? Find(byWeight[0]);
        return path is null ? null : new FontResolverInfo(path);
    }

    public byte[]? GetFont(string faceName) =>
        _cache.GetOrAdd(faceName, static name => File.Exists(name) ? File.ReadAllBytes(name) : null);

    private static string? Find(IEnumerable<string> fileNames) =>
        fileNames.SelectMany(name => Directories.Select(dir => Path.Combine(dir, name))).FirstOrDefault(File.Exists);

    private static string[] BuildDirectories()
    {
        var roots = new List<string>();
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        if (!string.IsNullOrEmpty(windows))
        {
            roots.Add(windows);
        }

        roots.AddRange(["/usr/share/fonts", "/usr/local/share/fonts", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".fonts"), "/Library/Fonts"]);

        // Linux distributions nest families in sub-directories; enumerate them once at start-up.
        return roots.Where(Directory.Exists)
            .SelectMany(root => new[] { root }.Concat(SafeSubdirectories(root)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<string> SafeSubdirectories(string root)
    {
        try
        {
            return Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException)
        {
            return [];
        }
    }
}
