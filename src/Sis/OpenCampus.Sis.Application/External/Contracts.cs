namespace OpenCampus.Sis.Application.External;

/// <summary>
/// External service contracts consumed by the SIS module (SDD 8.2: EXT-01 email, EXT-02 SMS, EXT-04 library
/// catalogue, EXT-05 records archive), declared here per 18.6: asynchronous, cancellable, and answered with a result
/// type that can express success, partial failure and total failure. Implementations live in infrastructure and are
/// selected by the composition root from configuration; no provider-specific type appears in these signatures (8.1).
/// </summary>
public enum ExternalOutcome
{
    Succeeded = 0,
    PartiallySucceeded = 1,
    Failed = 2,
}

/// <summary>One recipient or item the provider could not handle, with the provider's reason.</summary>
public sealed record ExternalFailure(string Target, string Reason);

/// <summary>The outcome of a dispatch: how far it got, the provider's reference for the accepted part, and what failed.</summary>
public sealed record ExternalResult(ExternalOutcome Outcome, string? Reference, IReadOnlyList<ExternalFailure> Failures)
{
    public static ExternalResult Success(string? reference) => new(ExternalOutcome.Succeeded, reference, []);

    public static ExternalResult Partial(string? reference, IReadOnlyList<ExternalFailure> failures) => new(ExternalOutcome.PartiallySucceeded, reference, failures);

    public static ExternalResult Failure(IReadOnlyList<ExternalFailure> failures) => new(ExternalOutcome.Failed, null, failures);

    /// <summary>Classifies a batch by how many of its targets were accepted.</summary>
    public static ExternalResult FromBatch(int targetCount, string? reference, IReadOnlyList<ExternalFailure> failures) =>
        failures.Count == 0 ? Success(reference)
        : failures.Count >= targetCount ? Failure(failures)
        : Partial(reference, failures);
}

// ----- EXT-01: e-mail dispatch -----

public sealed record EmailMessage(IReadOnlyList<string> To, string Subject, string Body);

public interface IEmailDispatcher
{
    Task<ExternalResult> SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

// ----- EXT-02: SMS dispatch -----

public sealed record SmsMessage(IReadOnlyList<string> To, string Text);

public interface ISmsDispatcher
{
    Task<ExternalResult> SendAsync(SmsMessage message, CancellationToken cancellationToken);
}

// ----- EXT-04: library catalogue lookup -----

public sealed record LibraryItem(string Identifier, string Title, string Author, int Year, bool Available);

public sealed record LibraryLookup(ExternalOutcome Outcome, IReadOnlyList<LibraryItem> Items, string? Failure);

public interface ILibraryCatalogue
{
    Task<LibraryLookup> SearchAsync(string term, int limit, CancellationToken cancellationToken);
}

// ----- EXT-05: records archive -----

/// <summary>A record handed to the institutional archive: what kind, which subject, and its content as JSON.</summary>
public sealed record ArchiveRecord(string RecordType, Guid SubjectId, string PayloadJson);

public interface IRecordsArchive
{
    Task<ExternalResult> ArchiveAsync(ArchiveRecord record, CancellationToken cancellationToken);
}
