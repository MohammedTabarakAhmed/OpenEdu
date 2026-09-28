namespace OpenCampus.Identity.Application.External;

/// <summary>
/// The Identity module's own copy of the EXT-01 e-mail contract (MB-04: each module declares the external shapes it
/// consumes; 18.6: asynchronous, cancellable, result-typed). Mirrors <c>OpenCampus.Sis.Application.External</c> without
/// referencing it — the modules share nothing but the composition root. Used for the self-registration messages.
/// </summary>
public enum ExternalOutcome
{
    Succeeded = 0,
    PartiallySucceeded = 1,
    Failed = 2,
}

public sealed record ExternalFailure(string Target, string Reason);

public sealed record ExternalResult(ExternalOutcome Outcome, string? Reference, IReadOnlyList<ExternalFailure> Failures)
{
    public static ExternalResult Success(string? reference) => new(ExternalOutcome.Succeeded, reference, []);

    public static ExternalResult Partial(string? reference, IReadOnlyList<ExternalFailure> failures) => new(ExternalOutcome.PartiallySucceeded, reference, failures);

    public static ExternalResult Failure(IReadOnlyList<ExternalFailure> failures) => new(ExternalOutcome.Failed, null, failures);

    public static ExternalResult FromBatch(int targetCount, string? reference, IReadOnlyList<ExternalFailure> failures) =>
        failures.Count == 0 ? Success(reference)
        : failures.Count >= targetCount ? Failure(failures)
        : Partial(reference, failures);
}

public sealed record EmailMessage(IReadOnlyList<string> To, string Subject, string Body);

public interface IEmailDispatcher
{
    Task<ExternalResult> SendAsync(EmailMessage message, CancellationToken cancellationToken);
}
