using ResultBoxes;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Exceptions;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace Sekiban.Pure.Events;

public record EventDocumentCommon(
    Guid Id,
    JsonNode Payload,
    string SortableUniqueId,
    int Version,
    Guid AggregateId,
    string AggregateGroup,
    string RootPartitionKey,
    string PayloadTypeName,
    DateTime TimeStamp,
    string PartitionKey,
    EventMetadata Metadata) : IEventPayload
{
    public ResultBox<IEvent> ToEvent<TEventPayload>(JsonSerializerOptions options) where TEventPayload : IEventPayload
    {
        var p = Payload.Deserialize<TEventPayload>(options);
        if (p == null)
        {
            return ResultBox<IEvent>.FromException(new SekibanEventTypeNotFoundException("Failed to deserialize payload"));
        }
        return new Event<TEventPayload>(Id, p, new PartitionKeys(AggregateId, AggregateGroup, RootPartitionKey), SortableUniqueId, Version, Metadata);
    }
}