namespace OpenCampus.SharedKernel;

public sealed class EntityNotFoundException : DomainException
{
    public EntityNotFoundException(string entityName, Guid id)
        : base($"{entityName} with id '{id}' was not found.")
    {
        EntityName = entityName;
        Id = id;
    }

    public string EntityName { get; }

    public Guid Id { get; }
}
