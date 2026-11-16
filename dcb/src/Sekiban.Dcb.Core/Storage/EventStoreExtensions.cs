using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.InMemory;
using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.Storage;

/// <summary>
///     Extension methods for IEventStore
/// </summary>
public static class EventStoreExtensions
{
    public static Task<IEnumerable<Event>> ReadAllEventsAsync(
        this IEventStore eventStore,
        SortableUniqueId? since = null,
        int? maxCount = null) =>
        ReadAllEventsAsync(eventStore, ResolveEventTypes(eventStore), since, maxCount);

    /// <summary>
    ///     Explicitly deserialize serializable events after reading them from the store.
    /// </summary>
    public static async Task<IEnumerable<Event>> ReadAllEventsAsync(
        this IEventStore eventStore,
        IEventTypes eventTypes,
        SortableUniqueId? since = null,
        int? maxCount = null) =>
        DeserializeEvents(await eventStore.ReadAllSerializableEventsAsync(since, maxCount), eventTypes);

    public static Task<IEnumerable<Event>> ReadEventsByTagAsync(
        this IEventStore eventStore,
        ITag tag,
        SortableUniqueId? since = null) =>
        ReadEventsByTagAsync(eventStore, tag, ResolveEventTypes(eventStore), since);

    public static async Task<IEnumerable<Event>> ReadEventsByTagAsync(
        this IEventStore eventStore,
        ITag tag,
        IEventTypes eventTypes,
        SortableUniqueId? since = null) =>
        DeserializeEvents(await eventStore.ReadSerializableEventsByTagAsync(tag, since), eventTypes);

    /// <summary>Reads and deserializes one event; returns <c>null</c> when no event with the id exists.</summary>
    public static Task<Event?> ReadEventAsync(
        this IEventStore eventStore,
        Guid eventId) =>
        ReadEventAsync(eventStore, eventId, ResolveEventTypes(eventStore));

    /// <summary>Reads and deserializes one event; returns <c>null</c> when no event with the id exists.</summary>
    public static async Task<Event?> ReadEventAsync(
        this IEventStore eventStore,
        Guid eventId,
        IEventTypes eventTypes) =>
        (await eventStore.ReadSerializableEventAsync(eventId))?.ToEvent(eventTypes);

    public static Task<(IReadOnlyList<Event> Events, IReadOnlyList<TagWriteResult> TagWrites)> WriteEventsAsync(
        this IEventStore eventStore,
        IEnumerable<Event> events) =>
        WriteEventsAsync(eventStore, events, ResolveEventTypes(eventStore));

    public static async Task<(IReadOnlyList<Event> Events, IReadOnlyList<TagWriteResult> TagWrites)> WriteEventsAsync(
        this IEventStore eventStore,
        IEnumerable<Event> events,
        IEventTypes eventTypes)
    {
        var serializableEvents = events
            .Select(evt => evt.ToSerializableEvent(eventTypes))
            .ToList();
        var written = await eventStore.WriteSerializableEventsAsync(serializableEvents);
        return ((IReadOnlyList<Event>)DeserializeEvents(written.Events, eventTypes), written.TagWrites);
    }

    public static Task<Event> WriteEventAsync(
        this IEventStore eventStore,
        Event evt) =>
        WriteEventAsync(eventStore, evt, ResolveEventTypes(eventStore));

    /// <summary>
    ///     Helper method to write a single typed event through the serializable-only store interface.
    /// </summary>
    public static async Task<Event> WriteEventAsync(
        this IEventStore eventStore,
        Event evt,
        IEventTypes eventTypes) =>
        (await eventStore.WriteEventsAsync(new[] { evt }, eventTypes)).Events[0];

    private static List<Event> DeserializeEvents(
        IEnumerable<SerializableEvent> events,
        IEventTypes eventTypes)
    {
        var materialized = new List<Event>();
        foreach (var serializableEvent in events)
        {
            materialized.Add(serializableEvent.ToEvent(eventTypes));
        }

        return materialized;
    }

    private static IEventTypes ResolveEventTypes(IEventStore eventStore)
    {
        // Core still type-checks the volatile test store here. That coupling is exactly why the type
        // cannot physically move out of this package before the next major version; the [Obsolete]
        // pointer to Sekiban.Dcb.Core.Testing is the part that can ship now.
#pragma warning disable CS0618
        if (eventStore is InMemoryEventStore inMemoryEventStore && inMemoryEventStore.EventTypes is not null)
#pragma warning restore CS0618
        {
            return inMemoryEventStore.EventTypes;
        }

        if (eventStore is IServiceProvider serviceProvider &&
            serviceProvider.GetService(typeof(IEventTypes)) is IEventTypes eventTypes)
        {
            return eventTypes;
        }

        return new ReflectionEventTypes();
    }
}
