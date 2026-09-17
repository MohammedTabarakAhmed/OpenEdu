using System.Text.Json;
using Microsoft.Extensions.Options;
using OpenCampus.Sis.Application.External;

namespace OpenCampus.Sis.Infrastructure.External;

/// <summary>
/// Shared mechanics of the local adapters (8.3): every dispatch is appended as one JSON line to a file beneath the
/// configured output directory, so the evidence is inspectable and deterministic. Writes are serialised per process
/// so concurrent dispatches never interleave within a line.
/// </summary>
internal static class LocalEvidence
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string ResolveRoot(NotificationOptions options, string contentRootPath) =>
        Path.GetFullPath(Path.IsPathRooted(options.OutputPath) ? options.OutputPath : Path.Combine(contentRootPath, options.OutputPath));

    public static async Task AppendAsync(string filePath, object entry, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        var line = JsonSerializer.Serialize(entry, Json) + Environment.NewLine;
        await Gate.WaitAsync(cancellationToken);
        try
        {
            await File.AppendAllTextAsync(filePath, line, cancellationToken);
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>Splits a batch into accepted targets and failures; blank targets and those failing <paramref name="isValid"/> are refused.</summary>
    public static (IReadOnlyList<string> Accepted, IReadOnlyList<ExternalFailure> Failures) Screen(IReadOnlyList<string> targets, Func<string, bool> isValid, string reason)
    {
        var accepted = new List<string>();
        var failures = new List<ExternalFailure>();
        foreach (var target in targets)
        {
            if (!string.IsNullOrWhiteSpace(target) && isValid(target))
            {
                accepted.Add(target.Trim());
            }
            else
            {
                failures.Add(new ExternalFailure(target ?? string.Empty, reason));
            }
        }

        return (accepted, failures);
    }
}

/// <summary>EXT-01 local adapter: e-mails are persisted to <c>email.jsonl</c> instead of being transmitted (DEP-02).</summary>
public sealed class LocalEmailDispatcher(IOptions<NotificationOptions> options, string contentRootPath, TimeProvider clock) : IEmailDispatcher
{
    private readonly string _file = Path.Combine(LocalEvidence.ResolveRoot(options.Value, contentRootPath), "email.jsonl");

    public async Task<ExternalResult> SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var (accepted, failures) = LocalEvidence.Screen(message.To, a => a.Contains('@') && !a.Contains(' '), "invalid e-mail address");
        if (accepted.Count == 0)
        {
            return ExternalResult.Failure(failures.Count == 0 ? [new ExternalFailure(string.Empty, "no recipients")] : failures);
        }

        var reference = $"email-{Guid.NewGuid():N}";
        await LocalEvidence.AppendAsync(_file, new
        {
            reference,
            dispatchedAtUtc = clock.GetUtcNow().UtcDateTime,
            from = options.Value.SenderIdentity,
            to = accepted,
            message.Subject,
            message.Body,
            rejected = failures,
        }, cancellationToken);

        return ExternalResult.FromBatch(message.To.Count, reference, failures);
    }
}

/// <summary>EXT-02 local adapter: SMS messages are persisted to <c>sms.jsonl</c> (DEP-02). Numbers must be digits with an optional leading '+'.</summary>
public sealed class LocalSmsDispatcher(IOptions<NotificationOptions> options, string contentRootPath, TimeProvider clock) : ISmsDispatcher
{
    private readonly string _file = Path.Combine(LocalEvidence.ResolveRoot(options.Value, contentRootPath), "sms.jsonl");

    public async Task<ExternalResult> SendAsync(SmsMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var (accepted, failures) = LocalEvidence.Screen(message.To, IsNumber, "invalid telephone number");
        if (accepted.Count == 0)
        {
            return ExternalResult.Failure(failures.Count == 0 ? [new ExternalFailure(string.Empty, "no recipients")] : failures);
        }

        var reference = $"sms-{Guid.NewGuid():N}";
        await LocalEvidence.AppendAsync(_file, new
        {
            reference,
            dispatchedAtUtc = clock.GetUtcNow().UtcDateTime,
            sender = options.Value.SenderIdentity,
            to = accepted,
            message.Text,
            rejected = failures,
        }, cancellationToken);

        return ExternalResult.FromBatch(message.To.Count, reference, failures);
    }

    private static bool IsNumber(string value)
    {
        var digits = value.Trim();
        if (digits.StartsWith('+'))
        {
            digits = digits[1..];
        }

        return digits.Length is >= 7 and <= 15 && digits.All(char.IsAsciiDigit);
    }
}

/// <summary>EXT-05 local adapter: each record is written once as <c>archive/{type}/{subject}.json</c>; re-archiving the same subject replaces it.</summary>
public sealed class LocalRecordsArchive(IOptions<NotificationOptions> options, string contentRootPath, TimeProvider clock) : IRecordsArchive
{
    private readonly string _root = Path.Combine(LocalEvidence.ResolveRoot(options.Value, contentRootPath), "archive");

    public async Task<ExternalResult> ArchiveAsync(ArchiveRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (string.IsNullOrWhiteSpace(record.RecordType) || record.RecordType.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))
        {
            return ExternalResult.Failure([new ExternalFailure(record.RecordType, "record type must be letters, digits or '-'")]);
        }

        try
        {
            using (JsonDocument.Parse(record.PayloadJson))
            {
            }
        }
        catch (JsonException e)
        {
            return ExternalResult.Failure([new ExternalFailure(record.SubjectId.ToString(), $"payload is not valid JSON: {e.Message}")]);
        }

        var directory = Path.Combine(_root, record.RecordType.ToLowerInvariant());
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{record.SubjectId:N}.json");
        var envelope = JsonSerializer.Serialize(new
        {
            record.RecordType,
            record.SubjectId,
            archivedAtUtc = clock.GetUtcNow().UtcDateTime,
            payload = JsonSerializer.Deserialize<JsonElement>(record.PayloadJson),
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
        await File.WriteAllTextAsync(path, envelope, cancellationToken);

        return ExternalResult.Success($"archive/{record.RecordType.ToLowerInvariant()}/{record.SubjectId:N}");
    }
}

/// <summary>
/// EXT-04 local adapter: a fixed, deterministic catalogue answered from memory, so the contract is exercised end to end
/// (search, bounded results, availability) without a provider. A blank term is a failed lookup, not an empty one.
/// </summary>
public sealed class LocalLibraryCatalogue : ILibraryCatalogue
{
    private static readonly IReadOnlyList<LibraryItem> Items =
    [
        new("ISBN-9780262033848", "Introduction to Algorithms", "Cormen, Leiserson, Rivest, Stein", 2009, true),
        new("ISBN-9780134685991", "Effective Java", "Joshua Bloch", 2018, true),
        new("ISBN-9780132350884", "Clean Code", "Robert C. Martin", 2008, false),
        new("ISBN-9780201633610", "Design Patterns", "Gamma, Helm, Johnson, Vlissides", 1994, true),
        new("ISBN-9781492041139", "Fundamentals of Software Architecture", "Richards, Ford", 2020, true),
        new("ISBN-9780321125217", "Domain-Driven Design", "Eric Evans", 2003, false),
        new("ISBN-9780262046305", "Introduction to Machine Learning", "Ethem Alpaydin", 2020, true),
        new("ISBN-9789953000001", "مقدمة في علوم الحاسب", "أحمد خليل", 2019, true),
    ];

    public Task<LibraryLookup> SearchAsync(string term, int limit, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return Task.FromResult(new LibraryLookup(ExternalOutcome.Failed, [], "a search term is required"));
        }

        var needle = term.Trim();
        var matches = Items
            .Where(i => i.Title.Contains(needle, StringComparison.OrdinalIgnoreCase) || i.Author.Contains(needle, StringComparison.OrdinalIgnoreCase) || i.Identifier.Equals(needle, StringComparison.OrdinalIgnoreCase))
            .Take(Math.Clamp(limit, 1, 50))
            .ToList();
        return Task.FromResult(new LibraryLookup(ExternalOutcome.Succeeded, matches, null));
    }
}
