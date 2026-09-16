using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCampus.Sis.Domain.Courses;
using OpenCampus.Sis.Domain.Programmes;

namespace OpenCampus.Sis.Infrastructure.Persistence.Configurations;

internal sealed class CourseConfiguration : IEntityTypeConfiguration<Course>
{
    public void Configure(EntityTypeBuilder<Course> builder)
    {
        builder.ToTable("Courses");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.Code).HasMaxLength(Course.CodeMaxLength).IsRequired();
        builder.Property(c => c.NameEn).HasMaxLength(Course.NameMaxLength).IsRequired();
        builder.Property(c => c.NameAr).HasMaxLength(Course.NameMaxLength).IsRequired();
        builder.Property(c => c.DescriptionEn).HasMaxLength(Course.DescriptionMaxLength);
        builder.Property(c => c.DescriptionAr).HasMaxLength(Course.DescriptionMaxLength);
        builder.Property(c => c.Credits).IsRequired();

        // 13.3: Programme 1-* Course (foreign key within the module); unique index on Code.
        builder.HasOne<Programme>().WithMany().HasForeignKey(c => c.ProgrammeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(c => c.Code).IsUnique().HasFilter("[IsDeleted] = 0");
        builder.HasIndex(c => c.ProgrammeId);
    }
}
