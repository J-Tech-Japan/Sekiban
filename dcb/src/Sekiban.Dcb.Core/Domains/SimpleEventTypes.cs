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

        JsonTypeInfo? effectiveTypeInfo = ResolveEffectiveTypeInfo(eventType);

        if (effectiveTypeInfo is not null)
        {
            // The metadata the binder reads names from is the SAME metadata this deserialize will bind with — the
            // caller's resolver, its modifiers and its converters all reflected in effectiveTypeInfo.Properties. The
            // bind goes through the caller's options; the typeInfo is only read, so the caller's options stay
            // untouched.
            return EventPayloadBinder.Deserialize(
                json,
                effectiveTypeInfo,
                eventTypeName,
                _policy,
                effectiveJson => JsonSerializer.Deserialize(effectiveJson, eventType, _jsonOptions) as IEventPayload);
        }

        // No effective metadata could be obtained — a resolver that cannot describe this type, a converter-only
        // contract. CompatibleCaseSensitive is the pre-G13 escape hatch and may still bind unchecked; every other
        // policy PROMISES a check, so it fails deterministically and visibly rather than silently reverting to the
        // behaviour issue #1074 was about.
        if (_policy == EventPayloadDeserializationPolicy.CompatibleCaseSensitive)
        {
            return JsonSerializer.Deserialize(json, eventType, _jsonOptions) as IEventPayload;
        }

        throw new SekibanEventPayloadBindingException(
            $"Event '{eventTypeName}' payload (CLR type '{eventType.FullName}') cannot be checked under the "
            + $"{_policy} policy because no JSON metadata could be resolved for it (the options carry a resolver that "
            + $"does not describe this type, or a converter-only contract). Register the type with a resolver that "
            + $"describes it, or use the CompatibleCaseSensitive policy to bind without the check.",
            eventTypeName,
            eventType.FullName ?? eventType.Name,
            offendingJsonName: null,
            expectedJsonName: null,
            payloadLocation: null);
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
    ///     Resolves the JsonTypeInfo the deserializer WILL bind with, so the binder checks the names the bind will use
    ///     — including any caller resolver modifier that renames or ignores a member, and any converter-driven
    ///     contract. This is what makes the reflection preflight semantically identical to the deserialize, rather than
    ///     an independent convention-based guess.
    ///     It never mutates the caller's options. Two cases:
    ///     <list type="number">
    ///         <item>
    ///             <description>
    ///                 The caller's options can already produce the metadata (they carry a resolver, or have been used
    ///                 once): <c>GetTypeInfo</c> returns the EXACT effective metadata, modifiers and all. Use it.
    ///             </description>
    ///         </item>
    ///         <item>
    ///             <description>
    ///                 The caller set no resolver at all: the real deserialize
    ///                 (<c>JsonSerializer.Deserialize(json, type, options)</c>) will auto-attach the default reflection
    ///                 resolver over these same options, with no custom modifiers. Reproduce exactly that on a faithful
    ///                 COPY of the caller's options — converters, naming policy and all — so the names checked are the
    ///                 names that path binds. The copy is a private object; the caller's options are untouched.
    ///             </description>
    ///         </item>
    ///     </list>
    ///     Returns null only when neither can produce metadata (a resolver that genuinely cannot describe the type),
    ///     which the caller turns into a deterministic, policy-visible failure rather than a silent pre-G13 bind.
    /// </summary>
    private JsonTypeInfo? ResolveEffectiveTypeInfo(Type eventType)
    {
        try
        {
            return _jsonOptions.GetTypeInfo(eventType);
        }
        catch (NotSupportedException)
        {
            // A resolver is present but cannot describe this type: the real deserialize hits the same wall. Do NOT
            // substitute a convention-based resolver — that would check names the deserialize does not use.
            if (_jsonOptions.TypeInfoResolver is not null)
            {
                return null;
            }
        }
        catch (InvalidOperationException)
        {
            if (_jsonOptions.TypeInfoResolver is not null)
            {
                return null;
            }
        }

        // No resolver on the caller's options. Mirror what the deserialize will do: default reflection resolver over a
        // faithful copy of the caller's options.
        try
        {
            var effective = new JsonSerializerOptions(_jsonOptions) { TypeInfoResolver = ReflectionResolver };
            return effective.GetTypeInfo(eventType);
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
