using Sekiban.Dcb.Events;

namespace Sekiban.Dcb.Domains;

/// <summary>
/// Interface for managing event types in the domain
/// </summary>
public interface IEventTypes
{
    /// <summary>
    /// Serialize an event payload to JSON string
    /// </summary>
    string SerializeEventPayload(IEventPayload payload);
    
    /// <summary>
    /// Deserialize JSON string to event payload based on event type name
    /// </summary>
    IEventPayload? DeserializeEventPayload(string eventTypeName, string json);
    
    /// <summary>
    /// Get the type for an event by its name
    /// </summary>
    Type? GetEventType(string eventTypeName);
}