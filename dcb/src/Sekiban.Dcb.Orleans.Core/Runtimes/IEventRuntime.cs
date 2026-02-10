using Sekiban.Dcb.Events;

namespace Sekiban.Dcb.Runtime;

/// <summary>
///     Event type management runtime.
///     Used by the Grain layer to serialize/deserialize events.
/// </summary>
public interface IEventRuntime
{
    string SerializeEventPayload(IEventPayload payload);
    IEventPayload? DeserializeEventPayload(string eventTypeName, string json);
    Type? GetEventType(string eventTypeName);
}
