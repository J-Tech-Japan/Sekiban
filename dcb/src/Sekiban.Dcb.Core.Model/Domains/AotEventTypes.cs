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
    private readonly Dictionary<string, Func<string, IEventPayload?>> _deserializers = new();
    private readonly Dictionary<Type, Func<IEventPayload, string>> _serializers = new();

    /// <summary>
    ///     Register an event type with AOT-compatible JsonTypeInfo.
    /// </summary>
    /// <typeparam name="T">The event payload type</typeparam>
    /// <param name="eventTypeName">The event type name</param>
    /// <param name="typeInfo">The JsonTypeInfo for serialization</param>
    public void Register<T>(string eventTypeName, JsonTypeInfo<T> typeInfo)
        where T : IEventPayload
    {
        _deserializers[eventTypeName] = json =>
            JsonSerializer.Deserialize(json, typeInfo);

        _serializers[typeof(T)] = payload =>
            JsonSerializer.Serialize((T)payload, typeInfo);
    }

    /// <inheritdoc />
    public IEventPayload? DeserializeEventPayload(string eventTypeName, string json)
    {
        return _deserializers.TryGetValue(eventTypeName, out var deserializer)
            ? deserializer(json)
            : null;
    }

    /// <inheritdoc />
    public string SerializeEventPayload(IEventPayload payload)
    {
        return _serializers.TryGetValue(payload.GetType(), out var serializer)
            ? serializer(payload)
            : "{}";
    }

    /// <summary>
    ///     Not supported in AOT mode. Returns null.
    /// </summary>
    public Type? GetEventType(string eventTypeName) => null;
}
