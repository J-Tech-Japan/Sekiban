using System.Text.Json;
using Sekiban.Dcb.Events;

namespace Sekiban.Dcb.Domains;

/// <summary>
/// Simple implementation of IEventTypes that manages event type registration
/// </summary>
public class SimpleEventTypes : IEventTypes
{
    private readonly Dictionary<string, Type> _eventTypes = new();
    private readonly JsonSerializerOptions _jsonOptions;
    
    public SimpleEventTypes(JsonSerializerOptions? jsonOptions = null)
    {
        _jsonOptions = jsonOptions ?? new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };
    }
    
    /// <summary>
    /// Register an event type with its name
    /// </summary>
    public void RegisterEventType<T>(string eventTypeName) where T : IEventPayload
    {
        _eventTypes[eventTypeName] = typeof(T);
    }
    
    /// <summary>
    /// Register an event type using the type's name
    /// </summary>
    public void RegisterEventType<T>() where T : IEventPayload
    {
        var type = typeof(T);
        _eventTypes[type.Name] = type;
    }
    
    /// <inheritdoc/>
    public string SerializeEventPayload(IEventPayload payload)
    {
        return JsonSerializer.Serialize(payload, payload.GetType(), _jsonOptions);
    }
    
    /// <inheritdoc/>
    public IEventPayload? DeserializeEventPayload(string eventTypeName, string json)
    {
        if (!_eventTypes.TryGetValue(eventTypeName, out var eventType))
        {
            return null;
        }
        
        return JsonSerializer.Deserialize(json, eventType, _jsonOptions) as IEventPayload;
    }
    
    /// <inheritdoc/>
    public Type? GetEventType(string eventTypeName)
    {
        return _eventTypes.TryGetValue(eventTypeName, out var type) ? type : null;
    }
}