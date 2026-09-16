using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCampus.Sis.Domain.Courses;
using OpenCampus.Sis.Domain.Sections;

namespace OpenCampus.Sis.Infrastructure.Persistence.Configurations;

internal sealed class CourseSectionConfiguration : IEntityTypeConfiguration<CourseSection>
{
    public void Configure(EntityTypeBuilder<CourseSection> builder)
    {
        builder.ToTable("CourseSections");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.Code).HasMaxLength(CourseSection.CodeMaxLength).IsRequired();
        builder.Property(s => s.TermName).HasMaxLength(CourseSection.TermNameMaxLength).IsRequired();
        builder.Property(s => s.StartDate).IsRequired();
        builder.Property(s => s.EndDate).IsRequired();
        builder.Property(s => s.Capacity).IsRequired();
        // MB-03: cross-module reference persisted as an identifier only; no navigation, no foreign key.
        builder.Property(s => s.InstructorUserId).IsRequired();
        builder.Property(s => s.DeliveryMode).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

        // 13.3: Course 1-* CourseSection; index on Status.
        builder.HasOne<Course>().WithMany().HasForeignKey(s => s.CourseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(s => s.Status);
        builder.HasIndex(s => new { s.CourseId, s.Code }).IsUnique().HasFilter("[IsDeleted] = 0");
        builder.HasIndex(s => s.InstructorUserId);

        builder.HasMany(s => s.Sessions).WithOne().HasForeignKey(x => x.SectionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(s => s.GradeComponents).WithOne().HasForeignKey(x => x.SectionId).OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(s => s.Sessions).AutoInclude(false);
        builder.Navigation(s => s.GradeComponents).AutoInclude(false);
        builder.Metadata.FindNavigation(nameof(CourseSection.Sessions))!.SetPropertyAccessMode(PropertyAccessMode.Field);
        builder.Metadata.FindNavigation(nameof(CourseSection.GradeComponents))!.SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(s => s.TotalWeightPercent);
    }
}

internal sealed class SessionConfiguration : IEntityTypeConfiguration<Session>
{
    public void Configure(EntityTypeBuilder<Session> builder)
    {
        builder.ToTable("Sessions");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.ScheduledStartUtc).IsRequired();
        builder.Property(s => s.ScheduledEndUtc).IsRequired();
        builder.Property(s => s.Location).HasMaxLength(Session.LocationMaxLength);

        builder.HasIndex(s => new { s.SectionId, s.ScheduledStartUtc });
    }
}

internal sealed class GradeComponentConfiguration : IEntityTypeConfiguration<GradeComponent>
{
    public void Configure(EntityTypeBuilder<GradeComponent> builder)
    {
        builder.ToTable("GradeComponents");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.NameEn).HasMaxLength(GradeComponent.NameMaxLength).IsRequired();
        builder.Property(c => c.NameAr).HasMaxLength(GradeComponent.NameMaxLength).IsRequired();
        // DC-05: exact decimal types with declared precision and scale.
        builder.Property(c => c.WeightPercent).HasPrecision(5, 2).IsRequired();
        builder.Property(c => c.MaxScore).HasPrecision(7, 2).IsRequired();

        builder.HasIndex(c => c.SectionId);
    }
}
