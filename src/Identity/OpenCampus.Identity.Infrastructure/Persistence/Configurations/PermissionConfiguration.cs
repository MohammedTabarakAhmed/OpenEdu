using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCampus.Identity.Domain.Permissions;

namespace OpenCampus.Identity.Infrastructure.Persistence.Configurations;

internal sealed class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.ToTable("Permissions");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.Code).HasMaxLength(Permission.CodeMaxLength).IsRequired();
        builder.Property(p => p.Description).HasMaxLength(Permission.DescriptionMaxLength).IsRequired();

        builder.HasIndex(p => p.Code).IsUnique();
    }
}
