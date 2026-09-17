using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenCampus.Lms.Domain.Content;

namespace OpenCampus.Lms.Infrastructure.Persistence.Configurations;

internal sealed class CourseContentConfiguration : IEntityTypeConfiguration<CourseContent>
{
    public void Configure(EntityTypeBuilder<CourseContent> builder)
    {
        builder.ToTable("CourseContents");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        // MB-03: cross-module reference persisted as an identifier only; no navigation, no foreign key.
        builder.Property(c => c.SectionId).IsRequired();
        builder.Property(c => c.TitleEn).HasMaxLength(CourseContent.TitleMaxLength).IsRequired();
        builder.Property(c => c.TitleAr).HasMaxLength(CourseContent.TitleMaxLength).IsRequired();
        builder.Property(c => c.SortOrder).IsRequired();

        // 13.4: index on SectionId.
        builder.HasIndex(c => new { c.SectionId, c.SortOrder });

        builder.HasMany(c => c.Items).WithOne().HasForeignKey(i => i.CourseContentId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(c => c.Items).AutoInclude(false);
        builder.Metadata.FindNavigation(nameof(CourseContent.Items))!.SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(c => c.ItemsVisibleToLearners);
        builder.Ignore(c => c.AllResources);
    }
}

internal sealed class ContentItemConfiguration : IEntityTypeConfiguration<ContentItem>
{
    public void Configure(EntityTypeBuilder<ContentItem> builder)
    {
        builder.ToTable("ContentItems");

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();

        builder.Property(i => i.TitleEn).HasMaxLength(ContentItem.TitleMaxLength).IsRequired();
        builder.Property(i => i.TitleAr).HasMaxLength(ContentItem.TitleMaxLength).IsRequired();
        // 13.6: reference set stored by name.
        builder.Property(i => i.ItemType).HasConversion<string>().HasMaxLength(20).IsRequired();
        // DC-06: bounded to the largest body any type permits.
        builder.Property(i => i.Body).HasMaxLength(ContentItem.PageBodyMaxLength);
        builder.Property(i => i.SortOrder).IsRequired();
        builder.Property(i => i.IsPublished).IsRequired();

        builder.HasIndex(i => new { i.CourseContentId, i.SortOrder });

        builder.HasMany(i => i.Resources).WithOne().HasForeignKey(r => r.ContentItemId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(i => i.Resources).AutoInclude(false);
        builder.Metadata.FindNavigation(nameof(ContentItem.Resources))!.SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(i => i.IsVisibleToLearners);
    }
}

internal sealed class ResourceConfiguration : IEntityTypeConfiguration<Resource>
{
    public void Configure(EntityTypeBuilder<Resource> builder)
    {
        builder.ToTable("Resources");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.FileName).HasMaxLength(Resource.FileNameMaxLength).IsRequired();
        // 18.5: relative path only; file content is never stored in the database.
        builder.Property(r => r.StoredPath).HasMaxLength(Resource.StoredPathMaxLength).IsRequired();
        builder.Property(r => r.ContentType).HasMaxLength(Resource.ContentTypeMaxLength).IsRequired();
        builder.Property(r => r.SizeBytes).IsRequired();
        // SEC-26: SHA-256 hex digest.
        builder.Property(r => r.ContentHash).HasMaxLength(Resource.ContentHashMaxLength).IsFixedLength().IsRequired();

        builder.HasIndex(r => r.ContentItemId);
        builder.HasIndex(r => r.StoredPath).IsUnique().HasFilter("[IsDeleted] = 0");
    }
}
