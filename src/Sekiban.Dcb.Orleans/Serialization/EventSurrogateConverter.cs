using Sekiban.Dcb.Events;
using System.Text.Json;
namespace Sekiban.Dcb.Orleans.Serialization;

[RegisterConverter]
public sealed class EventSurrogateConverter : IConverter<Event, EventSurrogate>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public Event ConvertFromSurrogate(in EventSurrogate surrogate)
    {
        // Deserialize the payload
        var payloadType = Type.GetType(surrogate.PayloadTypeName);
        if (payloadType == null)
        {
            throw new InvalidOperationException($"Could not find type {surrogate.PayloadTypeName}");
        }

        var payload = JsonSerializer.Deserialize(surrogate.PayloadJson, payloadType, JsonOptions) as IEventPayload;
        if (payload == null)
        {
            throw new InvalidOperationException($"Could not deserialize payload of type {surrogate.PayloadTypeName}");
        }

        var metadata = new EventMetadata(surrogate.CausationId, surrogate.CorrelationId, surrogate.ExecutedUser);

        return new Event(
            payload,
            surrogate.SortableUniqueIdValue,
            surrogate.EventType,
            surrogate.Id,
            metadata,
            surrogate.Tags);
    }

    public EventSurrogate ConvertToSurrogate(in Event value)
    {
        var payloadType = value.Payload.GetType();
        var payloadJson = JsonSerializer.Serialize(value.Payload, payloadType, JsonOptions);

        return new EventSurrogate
        {
            PayloadJson = payloadJson,
            PayloadTypeName = payloadType.AssemblyQualifiedName ?? payloadType.FullName ?? payloadType.Name,
            SortableUniqueIdValue = value.SortableUniqueIdValue,
            EventType = value.EventType,
            Id = value.Id,
            CausationId = value.EventMetadata.CausationId,
            CorrelationId = value.EventMetadata.CorrelationId,
            ExecutedUser = value.EventMetadata.ExecutedUser,
            Tags = value.Tags
        };
    }
}
