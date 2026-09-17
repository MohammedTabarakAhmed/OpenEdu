namespace OpenCampus.Sis.Infrastructure.External;

/// <summary>
/// SDD Appendix B "Notification": the selected implementation, the output location of the local adapters and the
/// sender identity. Bound from the "Notification" section and validated at start-up. Only the local implementation
/// exists under current constraints (DEP-02); naming any other value is a configuration error rather than a silent
/// fallback, so replacing a provider is a visible decision at the composition root (18.6).
/// </summary>
public sealed class NotificationOptions
{
    public const string SectionName = "Notification";

    public const string LocalImplementation = "Local";

    /// <summary>Which adapter set the composition root wires; "Local" is the only value currently supported.</summary>
    public string Implementation { get; set; } = LocalImplementation;

    /// <summary>Directory where the local adapters persist dispatched messages and archived records, relative to the content root or absolute.</summary>
    public string OutputPath { get; set; } = "data/notifications";

    /// <summary>The From identity stamped on every e-mail; the SMS sender label.</summary>
    public string SenderIdentity { get; set; } = "OpenCampus <no-reply@opencampus.local>";

    public bool Validate() =>
        string.Equals(Implementation, LocalImplementation, StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(OutputPath)
        && !string.IsNullOrWhiteSpace(SenderIdentity);
}
