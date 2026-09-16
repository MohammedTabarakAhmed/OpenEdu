namespace OpenCampus.Sis.Domain.Sections;

/// <summary>Delivery modes (reference data, SDD 13.6).</summary>
public enum DeliveryMode
{
    InPerson = 1,
    Online = 2,
    Blended = 3,
}

/// <summary>Lifecycle of a scheduled delivery instance. Enrolment is accepted only while Open (BR-03).</summary>
public enum SectionStatus
{
    Draft = 1,
    Open = 2,
    Closed = 3,
    Cancelled = 4,
}
