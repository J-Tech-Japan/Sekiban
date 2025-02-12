using System.Text.Json.Serialization;
using Sekiban.Pure.Documents;

namespace Sekiban.Pure.Events;

public record Event<TEventPayload>(
    Guid Id,
    TEventPayload Payload,
    PartitionKeys PartitionKeys,
    string SortableUniqueId,
    int Version,
    EventMetadata Metadata) : IEvent where TEventPayload : IEventPayload
{
    public IEventPayload GetPayload()
    {
        return Payload;
    }
}