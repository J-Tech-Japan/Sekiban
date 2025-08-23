using Orleans;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Common;
using System.Text.Json;

namespace Sekiban.Dcb.Orleans.Serialization;

/// <summary>
/// Surrogate for serializing Sekiban.Dcb.Events.Event in Orleans streams
/// </summary>
[GenerateSerializer]
public struct EventSurrogate
{
    [Id(0)] public string PayloadJson { get; set; }
    [Id(1)] public string PayloadTypeName { get; set; }
    [Id(2)] public string SortableUniqueIdValue { get; set; }
    [Id(3)] public string EventType { get; set; }
    [Id(4)] public Guid Id { get; set; }
    [Id(5)] public string CausationId { get; set; }
    [Id(6)] public string CorrelationId { get; set; }
    [Id(7)] public string ExecutedUser { get; set; }
    [Id(8)] public List<string> Tags { get; set; }
}

[RegisterConverter]
public sealed class EventSurrogateConverter : IConverter<Event, EventSurrogate>
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
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

        var metadata = new EventMetadata(
            surrogate.CausationId,
            surrogate.CorrelationId,
            surrogate.ExecutedUser
        );

        return new Event(
            payload,
            surrogate.SortableUniqueIdValue,
            surrogate.EventType,
            surrogate.Id,
            metadata,
            surrogate.Tags
        );
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