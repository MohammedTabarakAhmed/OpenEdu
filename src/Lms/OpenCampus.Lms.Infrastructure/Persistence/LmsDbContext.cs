using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using OpenCampus.SharedKernel;

namespace OpenCampus.Lms.Infrastructure.Persistence;

public sealed class LmsDbContext(DbContextOptions<LmsDbContext> options, TimeProvider timeProvider)
    : DbContext(options)
{
    public const string Schema = "lms";

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(LmsDbContext).Assembly);
        ApplyLogicalDeletionFilter(modelBuilder);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        StampAudit();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void StampAudit()
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;

        foreach (var entry in ChangeTracker.Entries<Entity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.MarkCreated(now, actorId: null);
                    break;
                case EntityState.Modified:
                    entry.Entity.MarkModified(now, actorId: null);
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
