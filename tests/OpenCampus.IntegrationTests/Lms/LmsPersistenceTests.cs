using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenCampus.Lms.Application.Abstractions;
using OpenCampus.Lms.Domain.Content;
using OpenCampus.Lms.Infrastructure.Persistence;

namespace OpenCampus.IntegrationTests.Lms;

/// <summary>SDD 13.4 schema and the course content aggregate's persistence (DC-03 logical deletion, unique stored path).</summary>
[Collection(ApiCollection.Name)]
public class LmsPersistenceTests(ApiFactory factory)
{
    private const string Sha256 = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08";

    [Fact]
    public async Task Migration_CreatesLmsTables()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LmsDbContext>();

        var tables = await db.Database
            .SqlQueryRaw<string>("SELECT TABLE_NAME AS [Value] FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'lms'")
            .ToListAsync();

        tables.ShouldBe(
            ["Announcements", "Assignments", "AttendanceRecords", "ContentItems", "CourseContents", "Resources", "Submissions", "__EFMigrationsHistory"],
            ignoreOrder: true);
    }

    [Fact]
    public async Task Aggregate_RoundTripsWithItemsAndResources_InSortOrder()
    {
        var sectionId = Guid.NewGuid();
        var content = CourseContent.Create(sectionId, "Week 1", "الأسبوع 1", 0);
        var second = content.AddItem("B", "ب", ContentItemType.Page, "b");
        var first = content.AddItem("A", "أ", ContentItemType.File, null);
        content.AttachResource(first.Id, "a.pdf", $"content/{sectionId:N}/{first.Id:N}/x.pdf", "application/pdf", 3, Sha256);
        content.ReorderItems([first.Id, second.Id]);

        using (var scope = factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ICourseContentRepository>().Add(content);
            await scope.ServiceProvider.GetRequiredService<ILmsUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        using (var scope = factory.Services.CreateScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<ICourseContentRepository>();
            var loaded = await repository.FindByIdAsync(content.Id, CancellationToken.None);

            loaded.ShouldNotBeNull();
            loaded.Items.Select(i => i.TitleEn).ShouldBe(["A", "B"]);
            loaded.Items.First().Resources.ShouldHaveSingleItem().ContentHash.ShouldBe(Sha256);
            loaded.CreatedAtUtc.ShouldNotBe(default); // DC-02

            var bySection = await repository.ListBySectionAsync(sectionId, CancellationToken.None);
            bySection.ShouldHaveSingleItem().Id.ShouldBe(content.Id);

            var byResource = await repository.FindByResourceIdAsync(loaded.Items.First().Resources.Single().Id, CancellationToken.None);
            byResource!.Id.ShouldBe(content.Id);
            (await repository.FindByResourceIdAsync(Guid.NewGuid(), CancellationToken.None)).ShouldBeNull();
        }
    }

    [Fact]
    public async Task Remove_IsLogical_AndHidesTheWholeHierarchy()
    {
        var content = CourseContent.Create(Guid.NewGuid(), "Week 2", "الأسبوع 2", 0);
        var item = content.AddItem("A", "أ", ContentItemType.File, null);
        content.AttachResource(item.Id, "a.pdf", $"content/{content.SectionId:N}/{item.Id:N}/y.pdf", "application/pdf", 3, Sha256);

        using (var scope = factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ICourseContentRepository>().Add(content);
            await scope.ServiceProvider.GetRequiredService<ILmsUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        using (var scope = factory.Services.CreateScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<ICourseContentRepository>();
            repository.Remove((await repository.FindByIdAsync(content.Id, CancellationToken.None))!);
            await scope.ServiceProvider.GetRequiredService<ILmsUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LmsDbContext>();
            (await db.CourseContents.AnyAsync(c => c.Id == content.Id)).ShouldBeFalse();
            (await db.ContentItems.AnyAsync(i => i.Id == item.Id)).ShouldBeFalse();
            (await db.Resources.AnyAsync(r => r.ContentItemId == item.Id)).ShouldBeFalse();
            // DC-03: the rows remain.
            (await db.CourseContents.IgnoreQueryFilters().CountAsync(c => c.Id == content.Id && c.IsDeleted)).ShouldBe(1);
            (await db.Resources.IgnoreQueryFilters().CountAsync(r => r.ContentItemId == item.Id && r.IsDeleted)).ShouldBe(1);
        }
    }

    [Fact]
    public async Task StoredPath_IsUniqueAcrossLiveResources()
    {
        var path = $"content/{Guid.NewGuid():N}/shared.pdf";
        var one = CourseContent.Create(Guid.NewGuid(), "One", "١", 0);
        one.AttachResource(one.AddItem("A", "أ", ContentItemType.File, null).Id, "a.pdf", path, "application/pdf", 1, Sha256);
        var two = CourseContent.Create(Guid.NewGuid(), "Two", "٢", 0);
        two.AttachResource(two.AddItem("B", "ب", ContentItemType.File, null).Id, "b.pdf", path, "application/pdf", 1, Sha256);

        using var scope = factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<ICourseContentRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<ILmsUnitOfWork>();
        repository.Add(one);
        repository.Add(two);

        await Should.ThrowAsync<DbUpdateException>(() => unitOfWork.SaveChangesAsync(CancellationToken.None));
    }
}
