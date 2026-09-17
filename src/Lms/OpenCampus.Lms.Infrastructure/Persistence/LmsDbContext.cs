using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using OpenCampus.Lms.Application.Abstractions;
using OpenCampus.Lms.Domain.Announcements;
using OpenCampus.Lms.Domain.Assessment;
using OpenCampus.Lms.Domain.Attendance;
using OpenCampus.Lms.Domain.Content;
using OpenCampus.SharedKernel;

namespace OpenCampus.Lms.Infrastructure.Persistence;

public sealed class LmsDbContext(DbContextOptions<LmsDbContext> options, TimeProvider timeProvider, ICurrentUser currentUser)
    : DbContext(options)
{
    public const string Schema = "lms";

    public DbSet<CourseContent> CourseContents => Set<CourseContent>();

    public DbSet<ContentItem> ContentItems => Set<ContentItem>();

    public DbSet<Resource> Resources => Set<Resource>();

    public DbSet<Assignment> Assignments => Set<Assignment>();

    public DbSet<Submission> Submissions => Set<Submission>();

    public DbSet<AttendanceRecord> AttendanceRecords => Set<AttendanceRecord>();

    public DbSet<Announcement> Announcements => Set<Announcement>();

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

    // DC-03: logical deletion with a global query filter on every entity.
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
