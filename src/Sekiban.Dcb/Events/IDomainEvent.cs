namespace Sekiban.Dcb.Events;

public interface IDomainEvent
{
    public Guid Id { get; }
    public string SortableUniqueId { get; }
    public IDomainEventPayload GetPayload();
    public DomainEventMetadata Metadata { get; }
}