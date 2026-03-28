using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Sekiban.Dcb.Events;

namespace Sekiban.Dcb.Domains;

/// <summary>
///     AOT-compatible implementation of IEventTypes.
///     Uses JsonTypeInfo for serialization instead of reflection.
/// </summary>
public sealed class AotEventTypes : IEventTypes
{
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly Dictionary<string, Type> _eventTypes = new();
    private readonly Dictionary<string, Func<string, IEventPayload?>> _deserializers = new();
    private readonly Dictionary<Type, Func<IEventPayload, string>> _serializers = new();
    private readonly Dictionary<string, JsonTypeInfo> _typeInfosByEventName = new();
    private readonly Dictionary<Type, JsonTypeInfo> _typeInfosByPayloadType = new();

    public AotEventTypes(JsonSerializerOptions? jsonOptions = null)
    {
        _jsonOptions = jsonOptions ?? new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
    }

    /// <summary>
    ///     Register an event type with AOT-compatible JsonTypeInfo.
    /// </summary>
    /// <typeparam name="T">The event payload type</typeparam>
    /// <param name="eventTypeName">The event type name</param>
    /// <param name="typeInfo">The JsonTypeInfo for serialization</param>
    public void Register<T>(string eventTypeName, JsonTypeInfo<T> typeInfo)
        where T : IEventPayload
    {
        EnsureEventRegistration(eventTypeName, typeof(T), typeInfo);
        _deserializers[eventTypeName] = json =>
            JsonSerializer.Deserialize(json, typeInfo);

        _serializers[typeof(T)] = payload =>
            JsonSerializer.Serialize((T)payload, typeInfo);
    }

    /// <summary>
    ///     Register an event type with a non-generic JsonTypeInfo.
    ///     This overload avoids reflection-based generic instantiation in AOT callers.
    /// </summary>
    /// <param name="eventTypeName">The event type name</param>
    /// <param name="eventPayloadType">The CLR event payload type</param>
    /// <param name="typeInfo">The JsonTypeInfo for serialization</param>
    public void Register(string eventTypeName, Type eventPayloadType, JsonTypeInfo typeInfo)
    {
        if (!typeof(IEventPayload).IsAssignableFrom(eventPayloadType))
        {
            throw new ArgumentException(
                $"Type '{eventPayloadType.FullName}' does not implement {nameof(IEventPayload)}.",
                nameof(eventPayloadType));
        }

        if (typeInfo.Type != eventPayloadType)
        {
            throw new ArgumentException(
                $"JsonTypeInfo type '{typeInfo.Type.FullName}' does not match event payload type '{eventPayloadType.FullName}'.",
                nameof(typeInfo));
        }

        EnsureEventRegistration(eventTypeName, eventPayloadType, typeInfo);
        _deserializers[eventTypeName] = json =>
            JsonSerializer.Deserialize(json, typeInfo) as IEventPayload;

        _serializers[eventPayloadType] = payload =>
            JsonSerializer.Serialize(payload, typeInfo);
    }

    /// <inheritdoc />
    public IEventPayload? DeserializeEventPayload(string eventTypeName, string json)
    {
        if (_deserializers.TryGetValue(eventTypeName, out var deserializer))
        {
            return deserializer(json);
        }

        if (!_eventTypes.TryGetValue(eventTypeName, out Type? eventType))
        {
            return null;
        }

        JsonTypeInfo typeInfo = _typeInfosByEventName.TryGetValue(eventTypeName, out JsonTypeInfo? registeredTypeInfo)
            ? registeredTypeInfo
            : _jsonOptions.GetTypeInfo(eventType);

        return JsonSerializer.Deserialize(json, typeInfo) as IEventPayload;
    }

    /// <inheritdoc />
    public string SerializeEventPayload(IEventPayload payload)
    {
        Type payloadType = payload.GetType();
        if (_serializers.TryGetValue(payloadType, out var serializer))
        {
            return serializer(payload);
        }

        JsonTypeInfo typeInfo = _typeInfosByPayloadType.TryGetValue(payloadType, out JsonTypeInfo? registeredTypeInfo)
            ? registeredTypeInfo
            : _jsonOptions.GetTypeInfo(payloadType);

        return JsonSerializer.Serialize(payload, typeInfo);
    }

    /// <summary>
    ///     Returns the registered CLR event payload type.
    /// </summary>
    public Type? GetEventType(string eventTypeName) =>
        _eventTypes.TryGetValue(eventTypeName, out Type? eventType) ? eventType : null;

    private void EnsureEventRegistration(string eventTypeName, Type eventPayloadType, JsonTypeInfo typeInfo)
    {
        if (_eventTypes.TryGetValue(eventTypeName, out Type? existingEventType) && existingEventType != eventPayloadType)
        {
            throw new InvalidOperationException(
                $"Event type name '{eventTypeName}' is already registered with type '{existingEventType.FullName}'. " +
                $"Cannot register it with different type '{eventPayloadType.FullName}'.");
        }

        if (_typeInfosByPayloadType.TryGetValue(eventPayloadType, out JsonTypeInfo? existingPayloadTypeInfo) &&
            existingPayloadTypeInfo.Type != typeInfo.Type)
        {
            throw new InvalidOperationException(
                $"Event payload type '{eventPayloadType.FullName}' is already registered with JsonTypeInfo type '{existingPayloadTypeInfo.Type.FullName}'.");
        }

        _eventTypes[eventTypeName] = eventPayloadType;
        _typeInfosByEventName[eventTypeName] = typeInfo;
        _typeInfosByPayloadType[eventPayloadType] = typeInfo;
    }
}
