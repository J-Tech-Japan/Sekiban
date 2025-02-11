using Sekiban.Pure.Documents;

namespace Sekiban.Pure.Events;

public record EventDocument<TEventPayload>(
    // [property:JsonPropertyName("id")]
    Guid Id,
    TEventPayload Payload,
    string SortableUniqueId,
    int Version,
    Guid AggregateId,
    string AggregateGroup,
    string RootPartitionKey,
    string PayloadTypeName,
    DateTime TimeStamp,
    string PartitionKey,
    EventMetadata Metadata) : IEventDocument where TEventPayload : IEventPayload
{
    public static EventDocument<TEventPayload> FromEvent(Event<TEventPayload> ev)
    {
        var sortableUniqueIdValue = new SortableUniqueIdValue(ev.SortableUniqueId);
        return new EventDocument<TEventPayload>(ev.Id, ev.Payload, ev.SortableUniqueId, ev.Version,
            ev.PartitionKeys.AggregateId, ev.PartitionKeys.Group,
            ev.PartitionKeys.RootPartitionKey, ev.Payload.GetType().Name, sortableUniqueIdValue.GetTicks() ,
            ev.PartitionKeys.ToPrimaryKeysString(), ev.Metadata);
    }
}