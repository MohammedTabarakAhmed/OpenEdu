namespace OpenCampus.Sis.Application.Grading;

/// <summary>Academic thresholds (SDD Appendix B, "Academic"). Bound from the "Academic" section and validated at start-up.</summary>
public sealed class AcademicOptions
{
    public const string SectionName = "Academic";

    /// <summary>Final grade (0–100) at or above which an enrolment qualifies for a certificate (BR-10).</summary>
    public decimal PassThresholdPercent { get; set; } = 50m;

    /// <summary>Attendance rate (0–100) below which an active enrolment is placed At Risk (BR-12).</summary>
    public decimal AttendanceThresholdPercent { get; set; } = 75m;

    public bool Validate() =>
        PassThresholdPercent is >= 0 and <= 100 && AttendanceThresholdPercent is >= 0 and <= 100;
}
