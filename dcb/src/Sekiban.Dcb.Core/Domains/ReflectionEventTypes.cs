using Sekiban.Dcb.Events;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;

namespace Sekiban.Dcb.Domains;

/// <summary>
///     Fallback event type resolver used by legacy paths that do not provide explicit domain event registrations.
/// </summary>
public sealed class ReflectionEventTypes : IEventTypes
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private readonly ConcurrentDictionary<string, Type?> _cache = new(StringComparer.Ordinal);

    public string SerializeEventPayload(IEventPayload payload) =>
        JsonSerializer.Serialize(payload, payload.GetType(), JsonOptions);

    public IEventPayload? DeserializeEventPayload(string eventTypeName, string json)
    {
        var eventType = GetEventType(eventTypeName);
        return eventType is null
            ? null
            : JsonSerializer.Deserialize(json, eventType, JsonOptions) as IEventPayload;
    }

    public Type? GetEventType(string eventTypeName) =>
        _cache.GetOrAdd(eventTypeName, static name =>
            AppDomain.CurrentDomain
                .GetAssemblies()
                .Where(a => !a.IsDynamic)
                .SelectMany(static a =>
                {
                    try
                    {
                        return a.GetTypes();
                    }
                    catch (ReflectionTypeLoadException ex)
                    {
                        return ex.Types.Where(t => t is not null)!;
                    }
                })
                .FirstOrDefault(t =>
                    t is not null &&
                    typeof(IEventPayload).IsAssignableFrom(t) &&
                    string.Equals(t.Name, name, StringComparison.Ordinal)));
}
