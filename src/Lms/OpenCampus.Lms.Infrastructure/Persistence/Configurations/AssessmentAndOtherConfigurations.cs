using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCampus.Lms.Domain.Announcements;
using OpenCampus.Lms.Domain.Assessment;
using OpenCampus.Lms.Domain.Attendance;

namespace OpenCampus.Lms.Infrastructure.Persistence.Configurations;

internal sealed class AssignmentConfiguration : IEntityTypeConfiguration<Assignment>
{
    public void Configure(EntityTypeBuilder<Assignment> builder)
    {
        builder.ToTable("Assignments");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        // MB-03: cross-module reference held as an identifier only.
        builder.Property(a => a.SectionId).IsRequired();
        builder.Property(a => a.TitleEn).HasMaxLength(Assignment.TitleMaxLength).IsRequired();
        builder.Property(a => a.TitleAr).HasMaxLength(Assignment.TitleMaxLength).IsRequired();
        builder.Property(a => a.Instructions).HasMaxLength(Assignment.InstructionsMaxLength);
        // DC-05: exact decimals.
        builder.Property(a => a.MaxScore).HasPrecision(7, 2).IsRequired();
        builder.Property(a => a.DueAtUtc).IsRequired();
        builder.Property(a => a.AllowLate).IsRequired();
        builder.Property(a => a.LatePenaltyPercent).HasPrecision(5, 2).IsRequired();
        builder.Property(a => a.IsPublished).IsRequired();

        // 13.4: index on SectionId; Assignment 1—* Submission owned by the aggregate.
        builder.HasIndex(a => a.SectionId);

        builder.HasMany(a => a.Submissions).WithOne().HasForeignKey(s => s.AssignmentId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(a => a.Submissions).AutoInclude(false);
        builder.Metadata.FindNavigation(nameof(Assignment.Submissions))!.SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class SubmissionConfiguration : IEntityTypeConfiguration<Submission>
{
    public void Configure(EntityTypeBuilder<Submission> builder)
    {
        builder.ToTable("Submissions");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.LearnerUserId).IsRequired();
        builder.Property(s => s.SubmittedAtUtc).IsRequired();
        builder.Property(s => s.TextBody).HasMaxLength(Submission.TextBodyMaxLength);
        builder.Property(s => s.StoredPath).HasMaxLength(Submission.StoredPathMaxLength);
        builder.Property(s => s.Score).HasPrecision(7, 2);
        builder.Property(s => s.Feedback).HasMaxLength(Submission.FeedbackMaxLength);
        builder.Property(s => s.OriginalityScore).HasPrecision(5, 2);

        // 13.4: unique on (AssignmentId, LearnerUserId).
        builder.HasIndex(s => new { s.AssignmentId, s.LearnerUserId }).IsUnique().HasFilter("[IsDeleted] = 0");
        builder.Ignore(s => s.IsGraded);
        builder.HasIndex(s => s.LearnerUserId);
    }
}

internal sealed class AttendanceRecordConfiguration : IEntityTypeConfiguration<AttendanceRecord>
{
    public void Configure(EntityTypeBuilder<AttendanceRecord> builder)
    {
        builder.ToTable("AttendanceRecords");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        // MB-03: SessionId, LearnerUserId and RecordedByUserId are cross-module identifiers.
        builder.Property(a => a.SessionId).IsRequired();
        builder.Property(a => a.LearnerUserId).IsRequired();
        // 13.6: reference set stored by name.
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(a => a.RecordedByUserId).IsRequired();
        builder.Property(a => a.RecordedAtUtc).IsRequired();

        // 13.4: unique on (SessionId, LearnerUserId).
        builder.HasIndex(a => new { a.SessionId, a.LearnerUserId }).IsUnique().HasFilter("[IsDeleted] = 0");
        builder.HasIndex(a => a.LearnerUserId);
        builder.Ignore(a => a.CountsAsAttended);
    }
}

internal sealed class AnnouncementConfiguration : IEntityTypeConfiguration<Announcement>
{
    public void Configure(EntityTypeBuilder<Announcement> builder)
    {
        builder.ToTable("Announcements");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        // 13.4: null SectionId denotes an institute-wide announcement.
        builder.Property(a => a.SectionId);
        builder.Property(a => a.TitleEn).HasMaxLength(Announcement.TitleMaxLength).IsRequired();
        builder.Property(a => a.TitleAr).HasMaxLength(Announcement.TitleMaxLength).IsRequired();
        builder.Property(a => a.Body).HasMaxLength(Announcement.BodyMaxLength).IsRequired();
        builder.Property(a => a.PublishedAtUtc).IsRequired();
        builder.Property(a => a.ExpiresAtUtc);

        builder.HasIndex(a => new { a.SectionId, a.PublishedAtUtc });
    }
}
