namespace Sekiban.Dcb.Events;

public record DomainEvent<TEventPayload>(
    Guid Id,
    TEventPayload Payload,
    string SortableUniqueId,
    DomainEventMetadata Metadata) : IDomainEvent where TEventPayload : IDomainEventPayload
{
    public IDomainEventPayload GetPayload() => Payload;
}