namespace OpenCampus.Lms.Application.External;

/// <summary>
/// External service contracts consumed by the LMS module (SDD 8.2: EXT-01 email, EXT-03 originality checking),
/// declared here per 18.6: asynchronous, cancellable, answered with a result type expressing success, partial
/// failure and total failure. Implementations live in infrastructure; the composition root selects them from
/// configuration. The LMS cannot share the SIS declarations (MB-04), so the shapes are restated here.
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

// ----- EXT-01: e-mail dispatch -----

public sealed record EmailMessage(IReadOnlyList<string> To, string Subject, string Body);

public interface IEmailDispatcher
{
    Task<ExternalResult> SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

// ----- EXT-03: originality checking -----

/// <summary>The text of a submission offered for checking, keyed by the submission it belongs to.</summary>
public sealed record OriginalityRequest(Guid SubmissionId, string Text);

/// <summary>The provider's verdict: a similarity percentage and its reference, or why it could not check.</summary>
public sealed record OriginalityReport(ExternalOutcome Outcome, decimal? SimilarityPercent, string? Reference, string? Failure);

public interface IOriginalityChecker
{
    Task<OriginalityReport> CheckAsync(OriginalityRequest request, CancellationToken cancellationToken);
}
