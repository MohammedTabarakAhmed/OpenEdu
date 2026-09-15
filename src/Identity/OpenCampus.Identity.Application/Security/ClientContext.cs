namespace OpenCampus.Identity.Application.Security;

/// <summary>Originating address and agent of the current request, captured for sessions and audit (SEC-31).</summary>
public sealed record ClientContext(string? IpAddress, string? UserAgent);
