using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCampus.Sis.Domain.Programmes;

namespace OpenCampus.Sis.Infrastructure.Persistence.Configurations;

internal sealed class ProgrammeConfiguration : IEntityTypeConfiguration<Programme>
{
    public void Configure(EntityTypeBuilder<Programme> builder)
    {
        builder.ToTable("Programmes");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.Code).HasMaxLength(Programme.CodeMaxLength).IsRequired();
        builder.Property(p => p.NameEn).HasMaxLength(Programme.NameMaxLength).IsRequired();
        builder.Property(p => p.NameAr).HasMaxLength(Programme.NameMaxLength).IsRequired();
        builder.Property(p => p.DurationMonths).IsRequired();
        builder.Property(p => p.IsActive).IsRequired();

        // 13.3: unique index on Code. Filtered so a logically deleted programme frees its code.
        builder.HasIndex(p => p.Code).IsUnique().HasFilter("[IsDeleted] = 0");
    }
}
