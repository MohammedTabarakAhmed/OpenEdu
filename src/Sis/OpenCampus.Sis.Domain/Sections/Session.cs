using OpenCampus.SharedKernel;

namespace OpenCampus.Sis.Domain.Sections;

/// <summary>A scheduled meeting of a section (SDD 13.3: CourseSection 1—* Session). Part of the section aggregate.</summary>
public sealed class Session : Entity
{
    public const int LocationMaxLength = 200;

    private Session()
    {
    }

    public Guid SectionId { get; private set; }

    public DateTime ScheduledStartUtc { get; private set; }

    public DateTime ScheduledEndUtc { get; private set; }

    public string? Location { get; private set; }

    internal static Session Create(Guid sectionId, DateTime scheduledStartUtc, DateTime scheduledEndUtc, string? location)
    {
        var session = new Session { SectionId = sectionId };
        session.Reschedule(scheduledStartUtc, scheduledEndUtc, location);
        return session;
    }

    internal void Reschedule(DateTime scheduledStartUtc, DateTime scheduledEndUtc, string? location)
    {
        if (scheduledEndUtc <= scheduledStartUtc)
        {
            throw new DomainException("scheduledEndUtc must be later than scheduledStartUtc.");
        }

        ScheduledStartUtc = DateTime.SpecifyKind(scheduledStartUtc, DateTimeKind.Utc);
        ScheduledEndUtc = DateTime.SpecifyKind(scheduledEndUtc, DateTimeKind.Utc);
        Location = Guard.OptionalText(location, nameof(location), LocationMaxLength);
    }

    /// <summary>Half-open interval comparison: back-to-back sessions do not overlap.</summary>
    public bool Overlaps(DateTime otherStartUtc, DateTime otherEndUtc) =>
        ScheduledStartUtc < otherEndUtc && otherStartUtc < ScheduledEndUtc;
}
