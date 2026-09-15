namespace OpenCampus.SharedKernel;

/// <summary>Classifies a failure so the host can translate it to a status code (SDD 15.2, 18.3).</summary>
public enum ErrorType
{
    Failure,
    Validation,
    Unauthorized,
    Forbidden,
    NotFound,
    Conflict,
    RuleViolation,
}

public sealed record Error(string Code, string Message, ErrorType Type = ErrorType.Failure)
{
    public static readonly Error None = new(string.Empty, string.Empty);

    public static Error Validation(string code, string message) => new(code, message, ErrorType.Validation);

    public static Error Unauthorized(string code, string message) => new(code, message, ErrorType.Unauthorized);

    public static Error Forbidden(string code, string message) => new(code, message, ErrorType.Forbidden);

    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);

    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);

    public static Error Rule(string code, string message) => new(code, message, ErrorType.RuleViolation);
}
