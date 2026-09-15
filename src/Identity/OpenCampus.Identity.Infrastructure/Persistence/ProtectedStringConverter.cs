using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace OpenCampus.Identity.Infrastructure.Persistence;

/// <summary>
/// Encrypts a string column at rest using ASP.NET Core Data Protection.
/// Applied to <c>User.MfaSecret</c> (SDD 13.2: "MfaSecret shall be protected at rest").
/// </summary>
internal sealed class ProtectedStringConverter(IDataProtector protector)
    : ValueConverter<string?, string>(
        plain => protector.Protect(plain!),
        cipher => protector.Unprotect(cipher))
{
    public const string Purpose = "OpenCampus.Identity.MfaSecret";
}
