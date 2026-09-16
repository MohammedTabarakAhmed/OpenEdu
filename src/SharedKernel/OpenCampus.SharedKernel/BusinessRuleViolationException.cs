namespace OpenCampus.SharedKernel;

/// <summary>
/// Raised by an aggregate when a catalogued business rule (SDD section 14) would be violated.
/// The host translates it to 422 with the rule reference (18.3, API-07).
/// </summary>
public sealed class BusinessRuleViolationException : DomainException
{
    public BusinessRuleViolationException(string ruleCode, string message)
        : base(message)
    {
        RuleCode = ruleCode;
    }

    /// <summary>The section 14 reference, e.g. "BR-01".</summary>
    public string RuleCode { get; }
}
