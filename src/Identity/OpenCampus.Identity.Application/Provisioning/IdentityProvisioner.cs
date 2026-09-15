using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenCampus.Identity.Application.Abstractions;
using OpenCampus.Identity.Application.Authorization;
using OpenCampus.Identity.Application.Security;
using OpenCampus.Identity.Domain.Permissions;
using OpenCampus.Identity.Domain.Roles;
using OpenCampus.Identity.Domain.Users;

namespace OpenCampus.Identity.Application.Provisioning;

/// <summary>A credential generated during provisioning, to be delivered by the host outside logs and responses (SEC-02).</summary>
public sealed record GeneratedCredential(string Purpose, string UserName, string Password);

/// <summary>
/// SDD 18.7: reference data (13.6) is provisioned idempotently on every startup; demonstration
/// identities only where enabled. Every operation is a no-op when the data already exists.
/// </summary>
public sealed class IdentityProvisioner(
    IRoleRepository roles,
    IPermissionRepository permissions,
    IUserRepository users,
    IIdentityUnitOfWork unitOfWork,
    IPasswordHasher passwordHasher,
    IOptions<ProvisioningOptions> options,
    ILogger<IdentityProvisioner> logger)
{
    public async Task<IReadOnlyList<GeneratedCredential>> RunAsync(CancellationToken cancellationToken)
    {
        var generated = new List<GeneratedCredential>();
        var settings = options.Value;

        if (settings.ReferenceDataEnabled)
        {
            await ProvisionReferenceDataAsync(cancellationToken);
        }

        if (settings.DemonstrationDataEnabled)
        {
            generated.AddRange(await ProvisionDemonstrationIdentitiesAsync(settings, cancellationToken));
        }

        return generated;
    }

    private async Task ProvisionReferenceDataAsync(CancellationToken cancellationToken)
    {
        var existingPermissions = (await permissions.GetAllAsync(cancellationToken)).ToDictionary(p => p.Code);
        var addedPermissions = 0;
        foreach (var (code, description) in Permissions.Catalogue)
        {
            if (existingPermissions.ContainsKey(code))
            {
                continue;
            }

            var permission = Permission.Create(code, description);
            permissions.Add(permission);
            existingPermissions[code] = permission;
            addedPermissions++;
        }

        var existingRoles = (await roles.GetAllAsync(cancellationToken)).ToDictionary(r => r.Name);
        var addedRoles = 0;
        var addedGrants = 0;
        foreach (var (roleName, description) in RoleNames.Descriptions)
        {
            if (!existingRoles.TryGetValue(roleName, out var role))
            {
                role = Role.Create(roleName, description);
                roles.Add(role);
                existingRoles[roleName] = role;
                addedRoles++;
            }

            // Default mappings are provisioned once; later administrative changes are not overridden.
            if (role.Permissions.Count == 0 && Permissions.DefaultRoleMappings.TryGetValue(roleName, out var codes))
            {
                foreach (var code in codes)
                {
                    if (role.GrantPermission(existingPermissions[code].Id))
                    {
                        addedGrants++;
                    }
                }
            }
        }

        if (addedPermissions + addedRoles + addedGrants > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        logger.LogInformation(
            "Identity reference data provisioned: {Permissions} permissions, {Roles} roles, {Grants} grants added",
            addedPermissions, addedRoles, addedGrants);
    }

    private async Task<IReadOnlyList<GeneratedCredential>> ProvisionDemonstrationIdentitiesAsync(ProvisioningOptions settings, CancellationToken cancellationToken)
    {
        var generated = new List<GeneratedCredential>();
        var rolesByName = (await roles.GetAllAsync(cancellationToken)).ToDictionary(r => r.Name);

        if (!await users.UserNameExistsAsync(settings.AdministratorUserName, cancellationToken))
        {
            var password = settings.AdministratorPassword ?? GeneratePassword();
            if (settings.AdministratorPassword is null)
            {
                generated.Add(new GeneratedCredential("Administrator", settings.AdministratorUserName, password));
            }

            var admin = User.Create(settings.AdministratorEmail, settings.AdministratorUserName, passwordHasher.Hash(password), "System Administrator", "مدير النظام");
            admin.AssignRole(rolesByName[RoleNames.Administrator].Id);
            users.Add(admin);
        }

        var demoPassword = settings.DemonstrationPassword;
        var demoAccounts = DemonstrationAccounts.All;
        var created = 0;
        foreach (var account in demoAccounts)
        {
            if (await users.UserNameExistsAsync(account.UserName, cancellationToken))
            {
                continue;
            }

            if (demoPassword is null)
            {
                demoPassword = GeneratePassword();
                generated.Add(new GeneratedCredential("Demonstration accounts", "(all demonstration users)", demoPassword));
            }

            var user = User.Create(account.Email, account.UserName, passwordHasher.Hash(demoPassword), account.FullNameEn, account.FullNameAr);
            user.AssignRole(rolesByName[account.Role].Id);
            users.Add(user);
            created++;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Identity demonstration data provisioned: {Users} accounts added", created + (generated.Count > 0 ? 1 : 0));
        return generated;
    }

    private static string GeneratePassword()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
        return string.Create(20, alphabet, (span, chars) =>
        {
            for (var i = 0; i < span.Length; i++)
            {
                span[i] = chars[RandomNumberGenerator.GetInt32(chars.Length)];
            }
        });
    }
}

/// <summary>Demonstration identities (DEP-11): one registrar, three instructors and forty learners.</summary>
public static class DemonstrationAccounts
{
    public sealed record Account(string UserName, string Email, string FullNameEn, string FullNameAr, string Role);

    private static readonly (string En, string Ar)[] LearnerNames =
    [
        ("Aisha Al-Farsi", "عائشة الفارسي"), ("Omar Haddad", "عمر حداد"), ("Layla Mansour", "ليلى منصور"), ("Yousef Karim", "يوسف كريم"),
        ("Noor Saleh", "نور صالح"), ("Khalid Rashid", "خالد راشد"), ("Maryam Nasser", "مريم ناصر"), ("Tariq Aziz", "طارق عزيز"),
        ("Huda Salim", "هدى سالم"), ("Faisal Amin", "فيصل أمين"), ("Rania Khoury", "رانيا خوري"), ("Sami Jaber", "سامي جابر"),
        ("Dina Fahmy", "دينا فهمي"), ("Bilal Hamdan", "بلال حمدان"), ("Salma Yasin", "سلمى ياسين"), ("Adel Qasim", "عادل قاسم"),
        ("Lina Barakat", "لينا بركات"), ("Hassan Darwish", "حسن درويش"), ("Zainab Awad", "زينب عوض"), ("Nabil Sharif", "نبيل شريف"),
        ("Mona Taha", "منى طه"), ("Rami Suleiman", "رامي سليمان"), ("Farah Zidan", "فرح زيدان"), ("Ibrahim Nour", "إبراهيم نور"),
        ("Sara Khalil", "سارة خليل"), ("Majid Othman", "ماجد عثمان"), ("Yasmin Hariri", "ياسمين حريري"), ("Walid Sabbagh", "وليد صباغ"),
        ("Amal Ghanem", "أمل غانم"), ("Jamal Issa", "جمال عيسى"), ("Reem Abbas", "ريم عباس"), ("Ziad Mahmoud", "زياد محمود"),
        ("Hala Nasr", "هالة نصر"), ("Karim Baz", "كريم باز"), ("Nadia Rizk", "نادية رزق"), ("Fadi Haj", "فادي حاج"),
        ("Lubna Saad", "لبنى سعد"), ("Anas Diab", "أنس دياب"), ("Ghada Murad", "غادة مراد"), ("Samir Halabi", "سمير حلبي"),
    ];

    public static readonly IReadOnlyList<Account> All =
    [
        new("registrar", "registrar@opencampus.local", "Rashid Al-Amin", "راشد الأمين", RoleNames.Registrar),
        new("instructor1", "instructor1@opencampus.local", "Dr. Fatima Haddad", "د. فاطمة حداد", RoleNames.Instructor),
        new("instructor2", "instructor2@opencampus.local", "Dr. Ahmed Mansour", "د. أحمد منصور", RoleNames.Instructor),
        new("instructor3", "instructor3@opencampus.local", "Dr. Salma Karim", "د. سلمى كريم", RoleNames.Instructor),
        .. LearnerNames.Select((name, index) => new Account(
            $"learner{index + 1:00}", $"learner{index + 1:00}@opencampus.local", name.En, name.Ar, RoleNames.Learner)),
    ];
}
