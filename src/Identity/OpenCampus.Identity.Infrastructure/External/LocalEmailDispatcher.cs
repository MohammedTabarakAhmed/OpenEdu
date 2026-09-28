using System.Text.Json;
using Microsoft.Extensions.Options;
using OpenCampus.Identity.Application.External;

namespace OpenCampus.Identity.Infrastructure.External;

/// <summary>
/// EXT-01 local adapter for the Identity module (DEP-02, CON-04): each e-mail is appended as one JSON line to
/// <c>email.jsonl</c> beneath the configured output directory — the same file the SIS adapter writes, stamped
/// <c>origin = "identity"</c>. Writes are serialised per process so concurrent dispatches never interleave.
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
        foreach (var target in message.To)
        {
            if (!string.IsNullOrWhiteSpace(target) && target.Contains('@') && !target.Contains(' '))
            {
                accepted.Add(target.Trim());
            }
            else
            {
                failures.Add(new ExternalFailure(target ?? string.Empty, "invalid e-mail address"));
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
            origin = "identity",
            dispatchedAtUtc = clock.GetUtcNow().UtcDateTime,
            from = options.Value.SenderIdentity,
            to = accepted,
            message.Subject,
            message.Body,
            rejected = failures,
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
