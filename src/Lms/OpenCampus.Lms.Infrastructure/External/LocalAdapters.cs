using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using OpenCampus.Lms.Application.External;

namespace OpenCampus.Lms.Infrastructure.External;

/// <summary>SDD Appendix B "Notification" as bound by the LMS: the same section the SIS binds, read independently (MB-04).</summary>
public sealed class NotificationOptions
{
    public const string SectionName = "Notification";

    public const string LocalImplementation = "Local";

    public string Implementation { get; set; } = LocalImplementation;

    public string OutputPath { get; set; } = "data/notifications";

    public string SenderIdentity { get; set; } = "OpenCampus <no-reply@opencampus.local>";

    public bool Validate() =>
        string.Equals(Implementation, LocalImplementation, StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(OutputPath)
        && !string.IsNullOrWhiteSpace(SenderIdentity);
}

/// <summary>
/// EXT-01 local adapter for the LMS (8.3): e-mails are appended as JSON lines to the same <c>email.jsonl</c> the SIS
/// adapter writes, so one file holds every message the platform "sent". Writes are serialised per process.
/// </summary>
public sealed class LocalEmailDispatcher(IOptions<NotificationOptions> options, string contentRootPath, TimeProvider clock) : IEmailDispatcher
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly string _file = Path.Combine(
        Path.GetFullPath(Path.IsPathRooted(options.Value.OutputPath) ? options.Value.OutputPath : Path.Combine(contentRootPath, options.Value.OutputPath)),
        "email.jsonl");

    public async Task<ExternalResult> SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        var accepted = new List<string>();
        var failures = new List<ExternalFailure>();
        foreach (var address in message.To)
        {
            if (!string.IsNullOrWhiteSpace(address) && address.Contains('@') && !address.Contains(' '))
            {
                accepted.Add(address.Trim());
            }
            else
            {
                failures.Add(new ExternalFailure(address ?? string.Empty, "invalid e-mail address"));
            }
        }

        if (accepted.Count == 0)
        {
            return ExternalResult.Failure(failures.Count == 0 ? [new ExternalFailure(string.Empty, "no recipients")] : failures);
        }

        var reference = $"email-{Guid.NewGuid():N}";
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        var line = JsonSerializer.Serialize(new
        {
            reference,
            dispatchedAtUtc = clock.GetUtcNow().UtcDateTime,
            from = options.Value.SenderIdentity,
            to = accepted,
            message.Subject,
            message.Body,
            rejected = failures,
            origin = "lms",
        }, Json) + Environment.NewLine;

        await Gate.WaitAsync(cancellationToken);
        try
        {
            await File.AppendAllTextAsync(_file, line, cancellationToken);
        }
        finally
        {
            Gate.Release();
        }

        return ExternalResult.FromBatch(message.To.Count, reference, failures);
    }
}

/// <summary>
/// EXT-03 local adapter: a deterministic stand-in for an originality service. The similarity score is derived from the
/// SHA-256 of the normalised text, so the same submission always yields the same verdict and the report reference can
/// be recomputed for inspection; a blank submission cannot be checked and is reported as a failure.
/// </summary>
public sealed class LocalOriginalityChecker : IOriginalityChecker
{
    public Task<OriginalityReport> CheckAsync(OriginalityRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var text = request.Text?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            return Task.FromResult(new OriginalityReport(ExternalOutcome.Failed, null, null, "nothing to check"));
        }

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(text.ToLowerInvariant()));
        var similarity = digest[0] % 101; // 0–100, deterministic per text
        var reference = $"orig-{request.SubmissionId:N}-{Convert.ToHexStringLower(digest.AsSpan(0, 6))}";
        return Task.FromResult(new OriginalityReport(ExternalOutcome.Succeeded, similarity, reference, null));
    }
}
