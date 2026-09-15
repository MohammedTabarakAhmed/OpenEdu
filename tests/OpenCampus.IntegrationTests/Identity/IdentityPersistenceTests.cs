using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenCampus.Identity.Domain.Users;
using OpenCampus.Identity.Infrastructure.Persistence;

namespace OpenCampus.IntegrationTests.Identity;

[Collection(ApiCollection.Name)]
public class IdentityPersistenceTests(ApiFactory factory)
{
    [Fact]
    public async Task Migration_CreatesIdentityTables()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var tables = await db.Database
            .SqlQueryRaw<string>("SELECT TABLE_NAME AS [Value] FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'identity'")
            .ToListAsync();

        tables.ShouldBe(
            ["AuditEvents", "Permissions", "RolePermissions", "Roles", "UserRoles", "UserSessions", "Users", "__EFMigrationsHistory"],
            ignoreOrder: true);
    }

    [Fact]
    public async Task MfaSecret_IsEncryptedAtRestAndRoundTrips()
    {
        const string plainSecret = "JBSWY3DPEHPK3PXP";
        var user = User.Create($"{Guid.NewGuid():N}@example.org", $"u{Guid.NewGuid():N}"[..20], "hash", "Test", "اختبار");
        user.BeginMfaEnrolment(plainSecret);
        user.ConfirmMfaEnrolment();

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

            var stored = await db.Database
                .SqlQuery<string>($"SELECT MfaSecret AS [Value] FROM [identity].[Users] WHERE Id = {user.Id}")
                .SingleAsync();
            stored.ShouldNotBe(plainSecret);
            stored.ShouldNotContain(plainSecret);

            var reloaded = await db.Users.SingleAsync(u => u.Id == user.Id);
            reloaded.MfaSecret.ShouldBe(plainSecret);
            reloaded.CreatedAtUtc.ShouldNotBe(default);

            db.Users.Remove(reloaded);
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task DuplicateEmail_IsRejectedByDatabase()
    {
        var email = $"{Guid.NewGuid():N}@example.org";
        var first = User.Create(email, $"a{Guid.NewGuid():N}"[..20], "hash", "One", "واحد");
        var second = User.Create(email, $"b{Guid.NewGuid():N}"[..20], "hash", "Two", "اثنان");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        db.Users.Add(first);
        await db.SaveChangesAsync();

        db.Users.Add(second);
        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync());

        db.Entry(second).State = EntityState.Detached;
        db.Users.Remove(first);
        await db.SaveChangesAsync();
    }
}
