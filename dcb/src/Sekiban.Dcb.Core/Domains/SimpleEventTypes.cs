using Sekiban.Dcb.Events;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
namespace Sekiban.Dcb.Domains;

/// <summary>
///     Simple implementation of IEventTypes that manages event type registration
/// </summary>
public class SimpleEventTypes : IEventTypes
{
    private readonly Dictionary<string, Type> _eventTypes = new();
    private readonly JsonSerializerOptions _jsonOptions;

    public SimpleEventTypes(JsonSerializerOptions? jsonOptions = null) =>
        _jsonOptions = jsonOptions ??
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                WriteIndented = false
            };

    /// <inheritdoc />
    public string SerializeEventPayload(IEventPayload payload)
    {
        Type payloadType = payload.GetType();
        JsonTypeInfo? typeInfo = TryResolveTypeInfo(payloadType);
        return typeInfo is not null
            ? JsonSerializer.Serialize(payload, typeInfo)
            : JsonSerializer.Serialize(payload, payloadType, _jsonOptions);
    }

    /// <inheritdoc />
    public IEventPayload? DeserializeEventPayload(string eventTypeName, string json)
    {
        if (!_eventTypes.TryGetValue(eventTypeName, out var eventType))
        {
            return null;
        }

        JsonTypeInfo? typeInfo = TryResolveTypeInfo(eventType);
        return typeInfo is not null
            ? JsonSerializer.Deserialize(json, typeInfo) as IEventPayload
            : JsonSerializer.Deserialize(json, eventType, _jsonOptions) as IEventPayload;
    }

    /// <inheritdoc />
    public Type? GetEventType(string eventTypeName) =>
        _eventTypes.TryGetValue(eventTypeName, out var type) ? type : null;

    /// <summary>
    ///     Register an event type with its name
    /// </summary>
    public void RegisterEventType<T>(string eventTypeName) where T : IEventPayload
    {
        var newType = typeof(T);
        if (_eventTypes.TryGetValue(eventTypeName, out var existingType))
        {
            if (existingType != newType)
            {
                throw new InvalidOperationException(
                    $"Event type name '{eventTypeName}' is already registered with type '{existingType.FullName}'. " +
                    $"Cannot register it with different type '{newType.FullName}'.");
            }
        }
        _eventTypes[eventTypeName] = newType;
    }

    /// <summary>
    ///     Register an event type with its name without using generic reflection.
    /// </summary>
    public void RegisterEventType(string eventTypeName, Type eventType)
    {
        if (!typeof(IEventPayload).IsAssignableFrom(eventType))
        {
            throw new ArgumentException(
                $"Type '{eventType.FullName}' must implement {nameof(IEventPayload)}.",
                nameof(eventType));
        }

        if (_eventTypes.TryGetValue(eventTypeName, out var existingType) && existingType != eventType)
        {
            throw new InvalidOperationException(
                $"Event type name '{eventTypeName}' is already registered with type '{existingType.FullName}'. " +
                $"Cannot register it with different type '{eventType.FullName}'.");
        }

        _eventTypes[eventTypeName] = eventType;
    }

    /// <summary>
    ///     Register an event type using the type's name
    /// </summary>
    public void RegisterEventType<T>() where T : IEventPayload
    {
        var type = typeof(T);
        RegisterEventType<T>(type.Name);
    }

    /// <summary>
    ///     Register an event type using the type's name without generic reflection.
    /// </summary>
    public void RegisterEventType(Type eventPayloadType)
    {
        ArgumentNullException.ThrowIfNull(eventPayloadType);
        RegisterEventType(eventPayloadType.Name, eventPayloadType);
    }

    private JsonTypeInfo? TryResolveTypeInfo(Type eventType)
    {
        try
        {
            return _jsonOptions.GetTypeInfo(eventType);
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }
}
