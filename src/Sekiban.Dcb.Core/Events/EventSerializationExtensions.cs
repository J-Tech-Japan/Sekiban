using ResultBoxes;
using Sekiban.Dcb.Domains;
using System.Text;
namespace Sekiban.Dcb.Events;

/// <summary>
///     Extension methods for converting between Event and SerializableEvent
/// </summary>
public static class EventSerializationExtensions
{
    /// <summary>
    ///     Convert Event to SerializableEvent using the provided domain types for serialization
    /// </summary>
    public static SerializableEvent ToSerializableEvent(this Event evt, IEventTypes eventTypes)
    {
        var payloadJson = eventTypes.SerializeEventPayload(evt.Payload);
        var payloadBytes = Encoding.UTF8.GetBytes(payloadJson);

        return new SerializableEvent(
            payloadBytes,
            evt.SortableUniqueIdValue,
            evt.Id,
            evt.EventMetadata,
            evt.Tags,
            evt.EventType);
    }

    /// <summary>
    ///     Convert SerializableEvent to Event using the provided domain types for deserialization
    /// </summary>
    public static ResultBox<Event> ToEvent(this SerializableEvent serializable, IEventTypes eventTypes)
    {
        try
        {
            var payloadJson = Encoding.UTF8.GetString(serializable.Payload);
            var payload = eventTypes.DeserializeEventPayload(serializable.EventPayloadName, payloadJson);

            if (payload == null)
            {
                return ResultBox.Error<Event>(
                    new InvalidOperationException(
                        $"Failed to deserialize event payload of type {serializable.EventPayloadName}. " +
                        "Make sure the event type is registered in DcbDomainTypes."));
            }

            return ResultBox.FromValue(
                new Event(
                    payload,
                    serializable.SortableUniqueIdValue,
                    serializable.EventPayloadName,
                    serializable.Id,
                    serializable.EventMetadata,
                    serializable.Tags));
        }
        catch (Exception ex)
        {
            return ResultBox.Error<Event>(ex);
        }
    }
}
