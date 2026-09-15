using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCampus.Identity.Domain.Audit;

namespace OpenCampus.Identity.Infrastructure.Persistence.Configurations;

internal sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        builder.ToTable("AuditEvents");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        // UserId is deliberately not a foreign key: an audit row must outlive the
        // account it describes and must never be affected by cascades (SEC-32).
        builder.Property(a => a.UserId);
        builder.Property(a => a.EventType).HasMaxLength(AuditEvent.EventTypeMaxLength).IsRequired();
        builder.Property(a => a.EntityName).HasMaxLength(AuditEvent.EntityNameMaxLength).IsRequired();
        builder.Property(a => a.EntityId);
        builder.Property(a => a.DetailsJson).HasMaxLength(AuditEvent.DetailsMaxLength);
        builder.Property(a => a.OccurredAtUtc).IsRequired();
        builder.Property(a => a.IpAddress).HasMaxLength(AuditEvent.IpAddressMaxLength);

        builder.HasIndex(a => a.OccurredAtUtc);
        builder.HasIndex(a => a.UserId);
    }
}
