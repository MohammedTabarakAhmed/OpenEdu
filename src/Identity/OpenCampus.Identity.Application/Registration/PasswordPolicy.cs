using System.Collections.Frozen;
using System.Reflection;
using OpenCampus.Identity.Application.Authentication;

namespace OpenCampus.Identity.Application.Registration;

/// <summary>
/// Password acceptance rules (OWASP Authentication Cheat Sheet, NIST 800-63B): length only — no composition rules —
/// plus a refusal of the account's own identifiers and of the most common passwords. Shared by self-registration,
/// administrative user creation and password change so the three paths cannot drift apart.
/// </summary>
public static class PasswordPolicy
{
    /// <summary>Shortest identifier fragment worth refusing; shorter user names would reject too many honest passwords.</summary>
    public const int IdentifierFragmentMinimumLength = 4;

    public static IReadOnlyList<string> Validate(string? password, string? userName = null, string? email = null)
    {
        var messages = new List<string>();
        if (string.IsNullOrEmpty(password))
        {
            return messages; // NotEmpty is reported by the caller's rule.
        }

        if (password.Length < PasswordRules.MinimumLength)
        {
            messages.Add($"Password must be at least {PasswordRules.MinimumLength} characters.");
        }

        if (password.Length > PasswordRules.MaximumLength)
        {
            messages.Add($"Password must not exceed {PasswordRules.MaximumLength} characters.");
        }

        if (ContainsFragment(password, userName))
        {
            messages.Add("Password must not contain the user name.");
        }

        if (ContainsFragment(password, LocalPart(email)))
        {
            messages.Add("Password must not contain the e-mail address.");
        }

        if (CommonPasswords.Contains(password))
        {
            messages.Add("Password is too common; choose a less predictable one.");
        }

        return messages;
    }

    private static bool ContainsFragment(string password, string? fragment)
    {
        var trimmed = fragment?.Trim();
        return trimmed is { Length: >= IdentifierFragmentMinimumLength }
            && password.Contains(trimmed, StringComparison.OrdinalIgnoreCase);
    }

    private static string? LocalPart(string? email)
    {
        var at = email?.IndexOf('@') ?? -1;
        return at > 0 ? email![..at] : email;
    }
}

/// <summary>
/// The 1 000 most common passwords (SecLists <c>Pwdb_top-1000.txt</c>, MIT — see DEPENDENCIES.md), embedded so the check
/// works offline (CON-04) and never sends a password anywhere. Loaded once, matched case-insensitively.
/// </summary>
public static class CommonPasswords
{
    private const string ResourceName = "OpenCampus.Identity.Application.Registration.common-passwords.txt";

    private static readonly Lazy<FrozenSet<string>> Set = new(Load);

    public static int Count => Set.Value.Count;

    public static bool Contains(string password) => Set.Value.Contains(password);

    private static FrozenSet<string> Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' is missing.");
        using var reader = new StreamReader(stream);

        var entries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.ReadLine() is { } line)
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0)
            {
                entries.Add(trimmed);
            }
        }

        return entries.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
    }
}
