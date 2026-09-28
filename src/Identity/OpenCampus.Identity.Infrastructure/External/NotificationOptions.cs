namespace OpenCampus.Identity.Infrastructure.External;

/// <summary>
/// The Identity module's binding of Appendix B "Notification" (MB-04: each module owns its copy). Same section, same
/// values and same validation as the SIS binding, so one configuration drives both adapters.
/// </summary>
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
