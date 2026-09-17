using OpenCampus.Lms.Domain.Content;
using OpenCampus.SharedKernel;

namespace OpenCampus.UnitTests.Lms.Domain;

/// <summary>Course content aggregate: hierarchy, items, publication (BR-15) and resources (SEC-23/26 invariants).</summary>
public class CourseContentTests
{
    private static readonly Guid SectionId = Guid.NewGuid();
    private const string Sha256 = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08";

    private static CourseContent Content(int sortOrder = 0) => CourseContent.Create(SectionId, "Week 1", "الأسبوع 1", sortOrder);

    private static ContentItem PublishedFileItem(CourseContent content)
    {
        var item = content.AddItem("Slides", "شرائح", ContentItemType.File, null);
        content.AttachResource(item.Id, "slides.pdf", "sections/x/items/y/z.pdf", "application/pdf", 1024, Sha256);
        content.PublishItem(item.Id);
        return item;
    }

    // ----- Creation and amendment -----

    [Fact]
    public void Create_TrimsTitlesAndStartsEmpty()
    {
        var content = CourseContent.Create(SectionId, "  Week 1 ", "الأسبوع 1", 2);

        content.SectionId.ShouldBe(SectionId);
        content.TitleEn.ShouldBe("Week 1");
        content.SortOrder.ShouldBe(2);
        content.Items.ShouldBeEmpty();
    }

    [Fact]
    public void Create_RejectsMissingSectionTitleOrNegativeOrder()
    {
        Should.Throw<DomainException>(() => CourseContent.Create(Guid.Empty, "T", "ع", 0));
        Should.Throw<DomainException>(() => CourseContent.Create(SectionId, " ", "ع", 0));
        Should.Throw<DomainException>(() => CourseContent.Create(SectionId, "T", "", 0));
        Should.Throw<DomainException>(() => CourseContent.Create(SectionId, "T", "ع", -1));
        Should.Throw<DomainException>(() => CourseContent.Create(SectionId, new string('x', CourseContent.TitleMaxLength + 1), "ع", 0));
    }

    // ----- Items -----

    [Fact]
    public void AddItem_AppendsInOrderAndStartsUnpublished()
    {
        var content = Content();

        var first = content.AddItem("Intro", "مقدمة", ContentItemType.Page, "Welcome");
        var second = content.AddItem("Reading", "قراءة", ContentItemType.Link, "https://example.org/reading");

        first.SortOrder.ShouldBe(0);
        second.SortOrder.ShouldBe(1);
        first.IsPublished.ShouldBeFalse();
        second.CourseContentId.ShouldBe(content.Id);
        content.Items.Count.ShouldBe(2);
    }

    [Fact]
    public void AddItem_ValidatesBodyByType()
    {
        var content = Content();

        Should.Throw<DomainException>(() => content.AddItem("Page", "ص", ContentItemType.Page, null));
        Should.Throw<DomainException>(() => content.AddItem("Page", "ص", ContentItemType.Page, new string('a', ContentItem.PageBodyMaxLength + 1)));
        Should.Throw<DomainException>(() => content.AddItem("Link", "ر", ContentItemType.Link, "not a url"));
        Should.Throw<DomainException>(() => content.AddItem("Link", "ر", ContentItemType.Link, "ftp://example.org/x"));
        Should.Throw<DomainException>(() => content.AddItem("Link", "ر", ContentItemType.Link, "javascript:alert(1)"));
        Should.Throw<DomainException>(() => content.AddItem("X", "س", (ContentItemType)99, null));

        content.AddItem("File", "م", ContentItemType.File, null).Body.ShouldBeNull();
        content.AddItem("Link", "ر", ContentItemType.Link, "https://example.org/x").Body.ShouldBe("https://example.org/x");
    }

    [Fact]
    public void AmendItem_KeepsTypeAndRejectsUnknownItem()
    {
        var content = Content();
        var item = content.AddItem("Intro", "مقدمة", ContentItemType.Page, "Welcome");

        content.AmendItem(item.Id, "Intro 2", "مقدمة 2", "Updated", 5);

        item.TitleEn.ShouldBe("Intro 2");
        item.Body.ShouldBe("Updated");
        item.SortOrder.ShouldBe(5);
        item.ItemType.ShouldBe(ContentItemType.Page);
        Should.Throw<EntityNotFoundException>(() => content.AmendItem(Guid.NewGuid(), "T", "ع", "b", 0));
    }

    [Fact]
    public void ReorderItems_RequiresEveryItemExactlyOnce()
    {
        var content = Content();
        var a = content.AddItem("A", "أ", ContentItemType.Page, "a");
        var b = content.AddItem("B", "ب", ContentItemType.Page, "b");

        Should.Throw<DomainException>(() => content.ReorderItems([a.Id]));
        Should.Throw<DomainException>(() => content.ReorderItems([a.Id, a.Id]));
        Should.Throw<DomainException>(() => content.ReorderItems([a.Id, Guid.NewGuid()]));

        content.ReorderItems([b.Id, a.Id]);
        b.SortOrder.ShouldBe(0);
        a.SortOrder.ShouldBe(1);
    }

    [Fact]
    public void RemoveItem_ReturnsItsResourcesForFileCleanup()
    {
        var content = Content();
        var item = PublishedFileItem(content);

        var removed = content.RemoveItem(item.Id);

        removed.Count.ShouldBe(1);
        removed[0].StoredPath.ShouldBe("sections/x/items/y/z.pdf");
        content.Items.ShouldBeEmpty();
        Should.Throw<EntityNotFoundException>(() => content.RemoveItem(item.Id));
    }

    // ----- BR-15: content is not visible to a learner until published -----

    [Fact]
    public void BR15_UnpublishedItem_IsRefusedToLearner()
    {
        var content = Content();
        var item = content.AddItem("Intro", "مقدمة", ContentItemType.Page, "Welcome");

        item.IsVisibleToLearners.ShouldBeFalse();
        var violation = Should.Throw<BusinessRuleViolationException>(item.EnsureVisibleToLearner);
        violation.RuleCode.ShouldBe("BR-15");
        content.FindItemForLearner(item.Id).ShouldBeNull();
        content.ItemsVisibleToLearners.ShouldBeEmpty();
    }

    [Fact]
    public void BR15_PublishedItem_IsVisible_AndUnpublishHidesItAgain()
    {
        var content = Content();
        var item = content.AddItem("Intro", "مقدمة", ContentItemType.Page, "Welcome");

        content.PublishItem(item.Id);
        item.IsVisibleToLearners.ShouldBeTrue();
        Should.NotThrow(item.EnsureVisibleToLearner);
        content.FindItemForLearner(item.Id).ShouldBeSameAs(item);
        content.ItemsVisibleToLearners.ShouldHaveSingleItem();

        content.UnpublishItem(item.Id);
        content.FindItemForLearner(item.Id).ShouldBeNull();
    }

    [Fact]
    public void Publish_FileItemWithoutResource_IsRefused()
    {
        var content = Content();
        var item = content.AddItem("Slides", "شرائح", ContentItemType.File, null);

        Should.Throw<DomainException>(() => content.PublishItem(item.Id));
        item.IsPublished.ShouldBeFalse();

        content.AttachResource(item.Id, "slides.pdf", "a/b/c.pdf", "application/pdf", 10, Sha256);
        content.PublishItem(item.Id);
        item.IsPublished.ShouldBeTrue();
    }

    // ----- Resources -----

    [Fact]
    public void AttachResource_RecordsDisplayNameStoredPathAndHash()
    {
        var content = Content();
        var item = content.AddItem("Slides", "شرائح", ContentItemType.File, null);

        var resource = content.AttachResource(item.Id, "C:\\Users\\x\\Week 1 Slides.PDF", "sections/s/items/i/f.pdf", "application/pdf", 2048, Sha256.ToUpperInvariant());

        // SEC-23: the client name is a display attribute only, reduced to its file name; the stored path is what the system generated.
        resource.FileName.ShouldBe("Week 1 Slides.PDF");
        resource.StoredPath.ShouldBe("sections/s/items/i/f.pdf");
        resource.ContentType.ShouldBe("application/pdf");
        resource.SizeBytes.ShouldBe(2048);
        resource.ContentHash.ShouldBe(Sha256); // SEC-26, normalised to lower case
        resource.ContentItemId.ShouldBe(item.Id);
        item.Resources.ShouldHaveSingleItem();
        content.AllResources.ShouldHaveSingleItem();
    }

    [Theory]
    [InlineData("../escape.pdf")]
    [InlineData("C:\\absolute\\path.pdf")]
    [InlineData("/rooted/path.pdf")]
    public void AttachResource_RejectsPathsOutsideTheStore(string storedPath)
    {
        var content = Content();
        var item = content.AddItem("Slides", "شرائح", ContentItemType.File, null);

        Should.Throw<DomainException>(() => content.AttachResource(item.Id, "x.pdf", storedPath, "application/pdf", 10, Sha256));
    }

    [Fact]
    public void AttachResource_RejectsBadHashSizeOrDuplicatePath()
    {
        var content = Content();
        var item = content.AddItem("Slides", "شرائح", ContentItemType.File, null);

        Should.Throw<DomainException>(() => content.AttachResource(item.Id, "x.pdf", "a/b.pdf", "application/pdf", 10, "abc"));
        Should.Throw<DomainException>(() => content.AttachResource(item.Id, "x.pdf", "a/b.pdf", "application/pdf", 10, new string('z', 64)));
        Should.Throw<DomainException>(() => content.AttachResource(item.Id, "x.pdf", "a/b.pdf", "application/pdf", 0, Sha256));
        Should.Throw<DomainException>(() => content.AttachResource(item.Id, " ", "a/b.pdf", "application/pdf", 10, Sha256));

        content.AttachResource(item.Id, "x.pdf", "a/b.pdf", "application/pdf", 10, Sha256);
        Should.Throw<DomainException>(() => content.AttachResource(item.Id, "y.pdf", "A/B.PDF", "application/pdf", 10, Sha256));
    }

    [Fact]
    public void DetachResource_RemovesAndReportsUnknown()
    {
        var content = Content();
        var item = PublishedFileItem(content);
        var resource = item.Resources.Single();

        content.DetachResource(item.Id, resource.Id).ShouldBeSameAs(resource);
        item.Resources.ShouldBeEmpty();
        Should.Throw<EntityNotFoundException>(() => content.DetachResource(item.Id, resource.Id));
        Should.Throw<EntityNotFoundException>(() => content.DetachResource(Guid.NewGuid(), resource.Id));
    }
}
