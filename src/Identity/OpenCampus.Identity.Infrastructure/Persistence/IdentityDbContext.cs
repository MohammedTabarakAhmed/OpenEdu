using System.Linq.Expressions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using OpenCampus.Identity.Application.Abstractions;
using OpenCampus.Identity.Domain.Audit;
using OpenCampus.Identity.Domain.Permissions;
using OpenCampus.Identity.Domain.Roles;
using OpenCampus.Identity.Domain.Sessions;
using OpenCampus.Identity.Domain.Users;
using OpenCampus.SharedKernel;

namespace OpenCampus.Identity.Infrastructure.Persistence;

public sealed class IdentityDbContext(
    DbContextOptions<IdentityDbContext> options,
    TimeProvider timeProvider,
    IDataProtectionProvider dataProtectionProvider,
    ICurrentUser currentUser)
    : DbContext(options)
{
    public const string Schema = "identity";

    public DbSet<User> Users => Set<User>();

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<Permission> Permissions => Set<Permission>();

    public DbSet<UserSession> UserSessions => Set<UserSession>();

    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IdentityDbContext).Assembly);

        var protector = dataProtectionProvider.CreateProtector(ProtectedStringConverter.Purpose);
        modelBuilder.Entity<User>()
            .Property(u => u.MfaSecret)
            .HasConversion(new ProtectedStringConverter(protector));

        ApplyLogicalDeletionFilter(modelBuilder);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        StampAudit();
        return base.SaveChangesAsync(cancellationToken);
    }

    // DC-02: creation and modification attributes are populated centrally, never by services.
    private void StampAudit()
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var actorId = currentUser.UserId;

        foreach (var entry in ChangeTracker.Entries<Entity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.MarkCreated(now, actorId);
                    break;
                case EntityState.Modified:
                    entry.Entity.MarkModified(now, actorId);
                    break;
            }
        }
    }

    private static void ApplyLogicalDeletionFilter(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (entityType.BaseType is not null || !typeof(Entity).IsAssignableFrom(entityType.ClrType))
            {
                continue;
            }

            var parameter = Expression.Parameter(entityType.ClrType, "e");
            var notDeleted = Expression.Not(Expression.Property(parameter, nameof(Entity.IsDeleted)));
            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(Expression.Lambda(notDeleted, parameter));
        }
    }
}
