using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using OpenCampus.SharedKernel;
using OpenCampus.Sis.Application.Abstractions;
using OpenCampus.Sis.Domain.Courses;
using OpenCampus.Sis.Domain.Enrolments;
using OpenCampus.Sis.Domain.Learners;
using OpenCampus.Sis.Domain.Programmes;
using OpenCampus.Sis.Domain.Sections;

namespace OpenCampus.Sis.Infrastructure.Persistence;

public sealed class SisDbContext(DbContextOptions<SisDbContext> options, TimeProvider timeProvider, ICurrentUser currentUser)
    : DbContext(options)
{
    public const string Schema = "sis";

    public DbSet<Programme> Programmes => Set<Programme>();

    public DbSet<Course> Courses => Set<Course>();

    public DbSet<CourseSection> Sections => Set<CourseSection>();

    public DbSet<Session> Sessions => Set<Session>();

    public DbSet<GradeComponent> GradeComponents => Set<GradeComponent>();

    public DbSet<Learner> Learners => Set<Learner>();

    public DbSet<Enrolment> Enrolments => Set<Enrolment>();

    public DbSet<GradeEntry> GradeEntries => Set<GradeEntry>();

    public DbSet<Certificate> Certificates => Set<Certificate>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SisDbContext).Assembly);
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
