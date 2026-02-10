using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;

namespace Sekiban.Dcb.Runtime.Native;

/// <summary>
///     Native C# implementation of IEventRuntime.
///     Delegates to DcbDomainTypes.EventTypes for event serialization/deserialization.
/// </summary>
public class NativeEventRuntime : IEventRuntime
{
    private readonly IEventTypes _eventTypes;

    public NativeEventRuntime(DcbDomainTypes domainTypes)
    {
        _eventTypes = domainTypes.EventTypes;
    }

    public string SerializeEventPayload(IEventPayload payload) =>
        _eventTypes.SerializeEventPayload(payload);

    public IEventPayload? DeserializeEventPayload(string eventTypeName, string json) =>
        _eventTypes.DeserializeEventPayload(eventTypeName, json);

    public Type? GetEventType(string eventTypeName) =>
        _eventTypes.GetEventType(eventTypeName);
}
