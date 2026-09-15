using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCampus.Identity.Domain.Sessions;
using OpenCampus.Identity.Domain.Users;

namespace OpenCampus.Identity.Infrastructure.Persistence.Configurations;

internal sealed class UserSessionConfiguration : IEntityTypeConfiguration<UserSession>
{
    public void Configure(EntityTypeBuilder<UserSession> builder)
    {
        builder.ToTable("UserSessions");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.UserId).IsRequired();
        builder.Property(s => s.FamilyId).IsRequired();
        builder.Property(s => s.RefreshTokenHash).HasMaxLength(UserSession.RefreshTokenHashMaxLength).IsRequired();
        builder.Property(s => s.IssuedAtUtc).IsRequired();
        builder.Property(s => s.ExpiresAtUtc).IsRequired();
        builder.Property(s => s.RevokedAtUtc);
        builder.Property(s => s.IpAddress).HasMaxLength(UserSession.IpAddressMaxLength);
        builder.Property(s => s.UserAgent).HasMaxLength(UserSession.UserAgentMaxLength);

        builder.HasIndex(s => s.UserId);
        builder.HasIndex(s => s.RefreshTokenHash).IsUnique();
        builder.HasIndex(s => s.FamilyId);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Ignore(s => s.IsRevoked);
    }
}
