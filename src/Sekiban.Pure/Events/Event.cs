using Sekiban.Pure.Documents;
namespace Sekiban.Pure.Events;

public record Event<TEventPayload>(
    Guid Id,
    TEventPayload Payload,
    PartitionKeys PartitionKeys,
    string SortableUniqueId,
    int Version,
    string EventPayloadTypeName) : IEvent where TEventPayload : IEventPayload
{
    public IEventPayload GetPayload() => Payload;
}
