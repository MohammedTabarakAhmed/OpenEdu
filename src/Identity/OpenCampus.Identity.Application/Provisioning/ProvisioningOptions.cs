namespace OpenCampus.Identity.Application.Provisioning;

/// <summary>SDD Appendix B, "Provisioning". Bound from the "Provisioning" section.</summary>
public sealed class ProvisioningOptions
{
    public const string SectionName = "Provisioning";

    /// <summary>Roles, permission catalogue and default mappings (13.6). Runs in every environment (18.7).</summary>
    public bool ReferenceDataEnabled { get; set; } = true;

    /// <summary>Demonstration identities (DEP-11). Development and demonstration environments only (18.7).</summary>
    public bool DemonstrationDataEnabled { get; set; }

    public string AdministratorUserName { get; set; } = "admin";

    public string AdministratorEmail { get; set; } = "admin@opencampus.local";

    /// <summary>
    /// Initial administrator password. Held outside source control (SDD 9.2); when absent, a random
    /// password is generated on first run and written to <see cref="CredentialsFilePath"/>.
    /// </summary>
    public string? AdministratorPassword { get; set; }

    /// <summary>Password shared by demonstration accounts; generated when absent, as above.</summary>
    public string? DemonstrationPassword { get; set; }

    /// <summary>Where generated credentials are written, relative to the content root; excluded from source control.</summary>
    public string CredentialsFilePath { get; set; } = "data/provisioning/credentials.txt";

    public bool Validate() =>
        !string.IsNullOrWhiteSpace(AdministratorUserName)
        && !string.IsNullOrWhiteSpace(AdministratorEmail)
        && !string.IsNullOrWhiteSpace(CredentialsFilePath);
}
