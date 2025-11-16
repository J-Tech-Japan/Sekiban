using Sekiban.Dcb.Events;
namespace Sekiban.Dcb.Orleans.Serialization;

[RegisterConverter]
public sealed class SerializableEventSurrogateConverter : IConverter<SerializableEvent, SerializableEventSurrogate>
{
    public SerializableEvent ConvertFromSurrogate(in SerializableEventSurrogate surrogate)
    {
        var metadata = new EventMetadata(surrogate.CausationId, surrogate.CorrelationId, surrogate.ExecutedUser);

        return new SerializableEvent(
            surrogate.Payload,
            surrogate.SortableUniqueIdValue,
            surrogate.Id,
            metadata,
            surrogate.Tags,
            surrogate.EventPayloadName);
    }

    public SerializableEventSurrogate ConvertToSurrogate(in SerializableEvent value)
    {
        return new SerializableEventSurrogate
        {
            Payload = value.Payload,
            SortableUniqueIdValue = value.SortableUniqueIdValue,
            Id = value.Id,
            CausationId = value.EventMetadata.CausationId,
            CorrelationId = value.EventMetadata.CorrelationId,
            ExecutedUser = value.EventMetadata.ExecutedUser,
            Tags = value.Tags,
            EventPayloadName = value.EventPayloadName
        };
    }
}
