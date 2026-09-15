using OpenCampus.SharedKernel;

namespace OpenCampus.UnitTests.SharedKernel;

public class EntityTests
{
    private sealed class TestEntity : Entity;

    [Fact]
    public void NewEntity_HasIdentifier()
    {
        var entity = new TestEntity();

        entity.Id.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public void MarkCreated_SetsCreationAudit()
    {
        var entity = new TestEntity();
        var at = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var actor = Guid.NewGuid();

        entity.MarkCreated(at, actor);

        entity.CreatedAtUtc.ShouldBe(at);
        entity.CreatedBy.ShouldBe(actor);
        entity.ModifiedAtUtc.ShouldBeNull();
    }

    [Fact]
    public void MarkModified_SetsModificationAudit()
    {
        var entity = new TestEntity();
        var at = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        var actor = Guid.NewGuid();

        entity.MarkModified(at, actor);

        entity.ModifiedAtUtc.ShouldBe(at);
        entity.ModifiedBy.ShouldBe(actor);
    }

    [Fact]
    public void MarkDeleted_SetsLogicalDeletionFlag()
    {
        var entity = new TestEntity();

        entity.MarkDeleted();

        entity.IsDeleted.ShouldBeTrue();
    }
}
