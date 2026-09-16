using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCampus.Sis.Domain.Enrolments;
using OpenCampus.Sis.Domain.Learners;
using OpenCampus.Sis.Domain.Sections;

namespace OpenCampus.Sis.Infrastructure.Persistence.Configurations;

internal sealed class LearnerConfiguration : IEntityTypeConfiguration<Learner>
{
    public void Configure(EntityTypeBuilder<Learner> builder)
    {
        builder.ToTable("Learners");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();

        // MB-03: cross-module reference to the Identity account, identifier only.
        builder.Property(l => l.UserId).IsRequired();
        builder.Property(l => l.LearnerNumber).HasMaxLength(Learner.LearnerNumberMaxLength).IsRequired();
        builder.Property(l => l.NationalId).HasMaxLength(Learner.NationalIdMaxLength);
        builder.Property(l => l.DateOfBirth);
        builder.Property(l => l.Gender).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(l => l.Phone).HasMaxLength(Learner.PhoneMaxLength);
        builder.Property(l => l.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

        // 13.3: unique index on LearnerNumber. One learner record per account.
        builder.HasIndex(l => l.LearnerNumber).IsUnique().HasFilter("[IsDeleted] = 0");
        builder.HasIndex(l => l.UserId).IsUnique().HasFilter("[IsDeleted] = 0");
        builder.HasIndex(l => l.Status);

        builder.Ignore(l => l.CanEnrol);
    }
}

internal sealed class EnrolmentConfiguration : IEntityTypeConfiguration<Enrolment>
{
    public void Configure(EntityTypeBuilder<Enrolment> builder)
    {
        builder.ToTable("Enrolments");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.EnrolledAtUtc).IsRequired();
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        // DC-05: exact decimal for the final grade.
        builder.Property(e => e.FinalGrade).HasPrecision(5, 2);
        builder.Property(e => e.CompletedAtUtc);

        // 13.3: Learner 1-* Enrolment; CourseSection 1-* Enrolment; unique index on (LearnerId, SectionId).
        // One row per pair as specified; re-enrolment after withdrawal reinstates that row (Enrolment.Reinstate).
        builder.HasOne<Learner>().WithMany().HasForeignKey(e => e.LearnerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CourseSection>().WithMany().HasForeignKey(e => e.SectionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => new { e.LearnerId, e.SectionId }).IsUnique().HasFilter("[IsDeleted] = 0");
        builder.HasIndex(e => new { e.SectionId, e.Status });

        builder.Ignore(e => e.IsActive);
    }
}

internal sealed class GradeEntryConfiguration : IEntityTypeConfiguration<GradeEntry>
{
    public void Configure(EntityTypeBuilder<GradeEntry> builder)
    {
        builder.ToTable("GradeEntries");

        builder.HasKey(g => g.Id);
        builder.Property(g => g.Id).ValueGeneratedNever();

        builder.Property(g => g.Score).HasPrecision(7, 2).IsRequired();
        builder.Property(g => g.IsReleased).IsRequired();
        builder.Property(g => g.GradedByUserId).IsRequired();
        builder.Property(g => g.GradedAtUtc).IsRequired();

        // 13.3: Enrolment 1-* GradeEntry; GradeComponent 1-* GradeEntry; unique index on (EnrolmentId, GradeComponentId).
        builder.HasOne<Enrolment>().WithMany().HasForeignKey(g => g.EnrolmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<GradeComponent>().WithMany().HasForeignKey(g => g.GradeComponentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(g => new { g.EnrolmentId, g.GradeComponentId }).IsUnique();
    }
}

internal sealed class CertificateConfiguration : IEntityTypeConfiguration<Certificate>
{
    public void Configure(EntityTypeBuilder<Certificate> builder)
    {
        builder.ToTable("Certificates");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.VerificationCode).HasMaxLength(Certificate.VerificationCodeMaxLength).IsRequired();
        builder.Property(c => c.IssuedAtUtc).IsRequired();
        builder.Property(c => c.FilePath).HasMaxLength(Certificate.FilePathMaxLength).IsRequired();

        // 13.3: Enrolment 1-1 Certificate; unique index on VerificationCode.
        builder.HasOne<Enrolment>().WithOne().HasForeignKey<Certificate>(c => c.EnrolmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(c => c.VerificationCode).IsUnique();
    }
}
