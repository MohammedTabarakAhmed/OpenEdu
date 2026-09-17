using OpenCampus.Lms.Application;
using OpenCampus.Lms.Application.Content;
using OpenCampus.Lms.Domain.Content;
using OpenCampus.SharedKernel;
using static OpenCampus.UnitTests.Lms.Application.LmsHarness;

namespace OpenCampus.UnitTests.Lms.Application;

/// <summary>
/// SEC-12 at the application layer: the assigned instructor and a section administrator manage; an enrolled
/// learner reads published content only (BR-15); everyone else is answered "not found" (API-06).
/// </summary>
public class ContentAuthorizationTests
{
    // ----- Reading the hierarchy -----

    [Fact]
    public async Task AssignedInstructor_SeesEverythingAndMayManage()
    {
        var h = new LmsHarness();
        await h.SeedAsync();

        var result = await h.As(AssignedInstructor).Content.GetSectionContentAsync(h.Section.Id, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.CanManage.ShouldBeTrue();
        result.Value.Contents.Single().Items.Count.ShouldBe(3);
    }

    [Fact]
    public async Task Administrator_SeesEverythingAndMayManage()
    {
        var h = new LmsHarness();
        await h.SeedAsync();

        var result = await h.AsAdministrator().Content.GetSectionContentAsync(h.Section.Id, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.CanManage.ShouldBeTrue();
        result.Value.Contents.Single().Items.Count.ShouldBe(3);
    }

    [Fact]
    public async Task EnrolledLearner_SeesPublishedItemsOnly_BR15()
    {
        var h = new LmsHarness();
        var (_, published, draft, fileItem, _) = await h.SeedAsync();

        var result = await h.As(EnrolledLearnerId).Content.GetSectionContentAsync(h.Section.Id, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.CanManage.ShouldBeFalse();
        result.Value.Contents.Single().Items.Select(i => i.Id).ShouldBe([published.Id, fileItem.Id]);
        result.Value.Contents.Single().Items.ShouldNotContain(i => i.Id == draft.Id);
    }

    [Fact]
    public async Task NonAssignedInstructor_OtherLearner_AndAnonymous_AreAnsweredNotFound()
    {
        var h = new LmsHarness();
        var (unit, _, _, _, _) = await h.SeedAsync();

        foreach (var caller in new[] { OtherInstructor, OtherLearner })
        {
            var section = await h.As(caller).Content.GetSectionContentAsync(h.Section.Id, CancellationToken.None);
            section.Error.ShouldBe(LmsErrors.SectionNotFound);
            var content = await h.Content.GetContentAsync(unit.Id, CancellationToken.None);
            content.Error.Type.ShouldBe(ErrorType.NotFound);
        }

        h.User.UserId = null;
        (await h.Content.GetSectionContentAsync(h.Section.Id, CancellationToken.None)).Error.Type.ShouldBe(ErrorType.NotFound);
    }

    [Fact]
    public async Task UnknownSection_IsNotFoundEvenForAdministrator()
    {
        var h = new LmsHarness();

        var result = await h.AsAdministrator().Content.GetSectionContentAsync(Guid.NewGuid(), CancellationToken.None);

        result.Error.Type.ShouldBe(ErrorType.NotFound);
    }

    // ----- Managing -----

    [Fact]
    public async Task OnlyManagers_MayCreateAmendPublishAndDelete()
    {
        var h = new LmsHarness();
        var (unit, _, draft, _, _) = await h.SeedAsync();
        var create = new CreateCourseContentRequest("Week 2", "الأسبوع 2", null);

        foreach (var caller in new[] { EnrolledLearnerId, OtherInstructor, OtherLearner })
        {
            h.As(caller);
            (await h.Content.CreateContentAsync(h.Section.Id, create, CancellationToken.None)).Error.Type.ShouldBe(ErrorType.NotFound);
            (await h.Content.UpdateContentAsync(unit.Id, new UpdateCourseContentRequest("X", "س", 0), CancellationToken.None)).Error.Type.ShouldBe(ErrorType.NotFound);
            (await h.Content.AddItemAsync(unit.Id, new CreateContentItemRequest("T", "ع", ContentItemType.Page, "b", null), CancellationToken.None)).Error.Type.ShouldBe(ErrorType.NotFound);
            (await h.Content.PublishItemAsync(unit.Id, draft.Id, CancellationToken.None)).Error.Type.ShouldBe(ErrorType.NotFound);
            (await h.Content.DeleteContentAsync(unit.Id, CancellationToken.None)).Error.Type.ShouldBe(ErrorType.NotFound);
        }

        draft.IsPublished.ShouldBeFalse();
        h.Repository.Items.Count.ShouldBe(1);

        h.As(AssignedInstructor);
        var created = await h.Content.CreateContentAsync(h.Section.Id, create, CancellationToken.None);
        created.IsSuccess.ShouldBeTrue();
        created.Value.SortOrder.ShouldBe(1); // appended after Week 1
        (await h.Content.PublishItemAsync(unit.Id, draft.Id, CancellationToken.None)).Value.IsPublished.ShouldBeTrue();
        (await h.AsAdministrator().Content.UnpublishItemAsync(unit.Id, draft.Id, CancellationToken.None)).Value.IsPublished.ShouldBeFalse();
    }

    [Fact]
    public async Task Instructor_ListsOnlyAssignedSections_Learner_OnlyEnrolledOnes()
    {
        var h = new LmsHarness();
        h.Access.AddSection(OtherInstructor);

        (await h.As(AssignedInstructor).Content.ListMySectionsAsync(CancellationToken.None)).Select(s => s.Id).ShouldBe([h.Section.Id]);
        (await h.As(OtherLearner).Content.ListMySectionsAsync(CancellationToken.None)).ShouldBeEmpty();

        var learners = new LearnerContentService(h.Access, h.User);
        h.As(EnrolledLearnerId);
        (await learners.ListMySectionsAsync(CancellationToken.None)).Select(s => s.Id).ShouldBe([h.Section.Id]);
        h.As(OtherLearner);
        (await learners.ListMySectionsAsync(CancellationToken.None)).ShouldBeEmpty();
    }

    // ----- Download (SEC-25) -----

    [Fact]
    public async Task Download_IsAllowedForManagersAndEnrolledLearner_RefusedForOthers()
    {
        var h = new LmsHarness();
        var (_, _, _, _, resource) = await h.SeedAsync();

        foreach (var caller in new[] { AssignedInstructor, EnrolledLearnerId })
        {
            var download = await h.As(caller).Resources.OpenForDownloadAsync(resource.Id, CancellationToken.None);
            download.IsSuccess.ShouldBeTrue();
            download.Value.FileName.ShouldBe("slides.pdf");
            download.Value.ContentHash.ShouldBe(resource.ContentHash);
            await download.Value.Content.DisposeAsync();
        }

        (await h.AsAdministrator().Resources.OpenForDownloadAsync(resource.Id, CancellationToken.None)).IsSuccess.ShouldBeTrue();

        foreach (var caller in new[] { OtherInstructor, OtherLearner })
        {
            (await h.As(caller).Resources.OpenForDownloadAsync(resource.Id, CancellationToken.None)).Error.Type.ShouldBe(ErrorType.NotFound);
        }

        (await h.As(AssignedInstructor).Resources.OpenForDownloadAsync(Guid.NewGuid(), CancellationToken.None)).Error.Type.ShouldBe(ErrorType.NotFound);
    }

    [Fact]
    public async Task Download_OfUnpublishedItemResource_IsRefusedToLearner_BR15_ButNotToManager()
    {
        var h = new LmsHarness();
        var (unit, _, _, fileItem, resource) = await h.SeedAsync();
        unit.UnpublishItem(fileItem.Id);

        (await h.As(EnrolledLearnerId).Resources.OpenForDownloadAsync(resource.Id, CancellationToken.None)).Error.Type.ShouldBe(ErrorType.NotFound);
        (await h.As(AssignedInstructor).Resources.OpenForDownloadAsync(resource.Id, CancellationToken.None)).IsSuccess.ShouldBeTrue();
    }
}
