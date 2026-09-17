using OpenCampus.SharedKernel;

namespace OpenCampus.Lms.Domain;

/// <summary>Argument checks shared by the LMS aggregates; failures are domain exceptions, not framework ones (LR-01).</summary>
internal static class Guard
{
    public static string RequireText(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException($"{name} is required.");
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new DomainException($"{name} must not exceed {maxLength} characters.");
        }

        return trimmed;
    }

    public static string? OptionalText(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return RequireText(value, name, maxLength);
    }

    public static int RequirePositive(int value, string name)
    {
        if (value < 1)
        {
            throw new DomainException($"{name} must be at least 1.");
        }

        return value;
    }

    public static Guid RequireId(Guid value, string name)
    {
        if (value == Guid.Empty)
        {
            throw new DomainException($"{name} is required.");
        }

        return value;
    }
}
