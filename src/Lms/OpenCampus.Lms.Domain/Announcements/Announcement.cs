using OpenCampus.SharedKernel;

namespace OpenCampus.Lms.Domain.Announcements;

/// <summary>
/// A notice to a section or, when SectionId is null, to the whole institute (SDD 13.4). Announcements are
/// mandatory scope (2.4) but the delivery plan (19) does not assign them to an increment before the
/// assessment work; the schema is created here with the other 13.4 entities and behaviour follows later.
/// </summary>
public sealed class Announcement : Entity
{
    public const int TitleMaxLength = 200;
    public const int BodyMaxLength = 8000;

    private Announcement()
    {
    }

    /// <summary>Cross-module reference (MB-03); null denotes an institute-wide announcement.</summary>
    public Guid? SectionId { get; private set; }

    public string TitleEn { get; private set; } = null!;

    public string TitleAr { get; private set; } = null!;

    public string Body { get; private set; } = null!;

    public DateTime PublishedAtUtc { get; private set; }

    public DateTime? ExpiresAtUtc { get; private set; }
}
