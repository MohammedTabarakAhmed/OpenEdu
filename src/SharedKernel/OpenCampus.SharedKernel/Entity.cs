namespace OpenCampus.SharedKernel;

public abstract class Entity
{
    protected Entity()
    {
        Id = SequentialGuid.NewGuid();
    }

    public Guid Id { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTime? ModifiedAtUtc { get; private set; }

    public Guid? ModifiedBy { get; private set; }

    public bool IsDeleted { get; private set; }

    public void MarkCreated(DateTime utcNow, Guid? actorId)
    {
        CreatedAtUtc = utcNow;
        CreatedBy = actorId;
    }

    public void MarkModified(DateTime utcNow, Guid? actorId)
    {
        ModifiedAtUtc = utcNow;
        ModifiedBy = actorId;
    }

    public void MarkDeleted()
    {
        IsDeleted = true;
    }
}
