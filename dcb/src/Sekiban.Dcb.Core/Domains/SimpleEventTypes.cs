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
    private readonly EventPayloadDeserializationPolicy _policy;

    public SimpleEventTypes(
        JsonSerializerOptions? jsonOptions = null,
        EventPayloadDeserializationPolicy policy = EventPayloadDeserializationPolicy.FailOnCaseMismatch)
    {
        _jsonOptions = jsonOptions ??
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                WriteIndented = false
            };
        _policy = policy;
    }

    /// <inheritdoc />
    public string SerializeEventPayload(IEventPayload payload)
    {
        Type payloadType = payload.GetType();
        JsonTypeInfo? typeInfo = TryResolveTypeInfo(payloadType);
        return typeInfo is not null
            ? JsonSerializer.Serialize(payload, typeInfo)
            : JsonSerializer.Serialize(payload, payloadType, _jsonOptions);
    }

    // Serialization is unchanged from pre-G13: resolve through the caller's own options when it can, otherwise let
    // JsonSerializer do it. This path writes, so it never needs the names-only resolver the read path uses.
    private JsonTypeInfo? TryResolveTypeInfo(Type payloadType)
    {
        try
        {
            return _jsonOptions.GetTypeInfo(payloadType);
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public IEventPayload? DeserializeEventPayload(string eventTypeName, string json)
    {
        if (!_eventTypes.TryGetValue(eventTypeName, out var eventType))
        {
            return null;
        }

        JsonTypeInfo? namesTypeInfo = TryResolveNamesTypeInfo(eventType);
        // When the metadata is available (the normal case), the shared binder applies the policy before binding. When
        // it is not — a resolver that cannot describe the type — there is nothing to check against, so fall back to the
        // pre-G13 behaviour rather than pretend to have validated it. The bind itself always goes through the caller's
        // options; the names typeInfo is only read, never used to deserialize, so the caller's options stay untouched.
        return namesTypeInfo is not null
            ? EventPayloadBinder.Deserialize(
                json,
                namesTypeInfo,
                eventTypeName,
                _policy,
                effectiveJson => JsonSerializer.Deserialize(effectiveJson, eventType, _jsonOptions) as IEventPayload)
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

    private static readonly DefaultJsonTypeInfoResolver ReflectionResolver = new();

    /// <summary>
    ///     Resolves a JsonTypeInfo ONLY so the binder can read the type's declared JSON member names. It is never used
    ///     to deserialize. It is built against a private, side-effect-free options that mirrors the caller's naming
    ///     policy — never the caller's own options — so the declared names come out correct (camelCase, etc.) while the
    ///     caller's options are neither read for a resolver nor written to. Returns null when even reflection cannot
    ///     describe the type, in which case the caller skips the preflight rather than fake a check it did not do.
    /// </summary>
    private JsonTypeInfo? TryResolveNamesTypeInfo(Type eventType)
    {
        try
        {
            var namesOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = _jsonOptions.PropertyNamingPolicy,
                PropertyNameCaseInsensitive = _jsonOptions.PropertyNameCaseInsensitive,
                TypeInfoResolver = ReflectionResolver
            };
            return namesOptions.GetTypeInfo(eventType);
        }
        catch (NotSupportedException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
