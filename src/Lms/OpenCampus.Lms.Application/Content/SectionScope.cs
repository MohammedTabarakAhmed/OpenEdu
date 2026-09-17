using OpenCampus.Lms.Application.Abstractions;

namespace OpenCampus.Lms.Application.Content;

/// <summary>The caller's relationship to a section, resolved through the SIS contract.</summary>
public enum SectionRole
{
    /// <summary>The caller neither manages nor is enrolled in the section: it is invisible to them (API-06).</summary>
    None = 0,

    /// <summary>Active enrolment: published content only (BR-15).</summary>
    EnrolledLearner = 1,

    /// <summary>The assigned instructor, or a holder of section administration: full view and management.</summary>
    Manager = 2,
}

public sealed record SectionScope(SectionSummary Section, SectionRole Role)
{
    public bool CanManage => Role == SectionRole.Manager;
}

/// <summary>
/// SEC-12 resource-level authorisation for everything the LMS holds against a section. A permission code
/// grants capability only (the controller policies); scope is established here per instance: the instructor
/// must be the one assigned, the learner must hold an active enrolment. Anything else resolves to
/// <see cref="SectionRole.None"/>, which services report as not found (API-06).
/// </summary>
public sealed class SectionScopeResolver(ISectionAccess sections, ICurrentUser currentUser)
{
    public async Task<SectionScope?> ResolveAsync(Guid sectionId, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return null;
        }

        var section = await sections.FindSectionAsync(sectionId, cancellationToken);
        if (section is null)
        {
            return null;
        }

        if (section.InstructorUserId == userId || currentUser.HasPermission(KnownPermissions.SectionAdministration))
        {
            return new SectionScope(section, SectionRole.Manager);
        }

        if (await sections.IsLearnerEnrolledAsync(userId, sectionId, cancellationToken))
        {
            return new SectionScope(section, SectionRole.EnrolledLearner);
        }

        return new SectionScope(section, SectionRole.None);
    }

    /// <summary>The scope when the caller may manage the section's content, else null.</summary>
    public async Task<SectionScope?> ResolveForManagementAsync(Guid sectionId, CancellationToken cancellationToken)
    {
        var scope = await ResolveAsync(sectionId, cancellationToken);
        return scope is { CanManage: true } ? scope : null;
    }

    /// <summary>The scope when the caller may read the section's content in any role, else null.</summary>
    public async Task<SectionScope?> ResolveForReadingAsync(Guid sectionId, CancellationToken cancellationToken)
    {
        var scope = await ResolveAsync(sectionId, cancellationToken);
        return scope is { Role: not SectionRole.None } ? scope : null;
    }
}
