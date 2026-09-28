using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCampus.Identity.Domain.Users;

namespace OpenCampus.Identity.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");

        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).ValueGeneratedNever();

        builder.Property(u => u.Email).HasMaxLength(User.EmailMaxLength).IsRequired();
        builder.Property(u => u.UserName).HasMaxLength(User.UserNameMaxLength).IsRequired();
        builder.Property(u => u.PasswordHash).HasMaxLength(512).IsRequired();
        builder.Property(u => u.FullNameEn).HasMaxLength(User.FullNameMaxLength).IsRequired();
        builder.Property(u => u.FullNameAr).HasMaxLength(User.FullNameMaxLength).IsRequired();
        builder.Property(u => u.IsActive).IsRequired();
        builder.Property(u => u.MfaEnabled).IsRequired();
        // Encrypted at rest; the converter is attached by the context because it needs a runtime protector.
        builder.Property(u => u.MfaSecret).HasMaxLength(1024);
        builder.Property(u => u.FailedLoginCount).IsRequired();
        builder.Property(u => u.LockedUntilUtc);

        // Self-registration state (Increment 7). Enums are stored by name (13.6).
        builder.Property(u => u.RegistrationStatus)
            .HasConversion<string>()
            .HasMaxLength(32)
            .HasDefaultValue(RegistrationStatus.None)
            .IsRequired();
        builder.Property(u => u.RequestedRole).HasMaxLength(User.RoleNameMaxLength);
        builder.Property(u => u.EmailVerifiedAtUtc);
        builder.Property(u => u.VerificationTokenHash).HasMaxLength(User.VerificationTokenHashMaxLength);
        builder.Property(u => u.VerificationTokenIssuedAtUtc);
        builder.Property(u => u.VerificationTokenExpiresAtUtc);

        builder.HasIndex(u => u.Email).IsUnique();
        builder.HasIndex(u => u.UserName).IsUnique();
        builder.HasIndex(u => u.VerificationTokenHash)
            .IsUnique()
            .HasFilter("[VerificationTokenHash] IS NOT NULL");
        builder.HasIndex(u => u.RegistrationStatus);

        builder.HasMany(u => u.Roles)
            .WithOne()
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(u => u.Roles).AutoInclude(false);
        builder.Metadata.FindNavigation(nameof(User.Roles))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
