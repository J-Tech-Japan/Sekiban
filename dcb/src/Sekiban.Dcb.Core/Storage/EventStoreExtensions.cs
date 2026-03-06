using ResultBoxes;
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
    public static Task<ResultBox<IEnumerable<Event>>> ReadAllEventsAsync(
        this IEventStore eventStore,
        SortableUniqueId? since = null,
        int? maxCount = null) =>
        ReadAllEventsAsync(eventStore, ResolveEventTypes(eventStore), since, maxCount);

    /// <summary>
    ///     Explicitly deserialize serializable events after reading them from the store.
    /// </summary>
    public static async Task<ResultBox<IEnumerable<Event>>> ReadAllEventsAsync(
        this IEventStore eventStore,
        IEventTypes eventTypes,
        SortableUniqueId? since = null,
        int? maxCount = null)
    {
        var result = await eventStore.ReadAllSerializableEventsAsync(since, maxCount);
        if (!result.IsSuccess)
        {
            return ResultBox.Error<IEnumerable<Event>>(result.GetException());
        }

        return DeserializeEvents(result.GetValue(), eventTypes);
    }

    public static Task<ResultBox<IEnumerable<Event>>> ReadEventsByTagAsync(
        this IEventStore eventStore,
        ITag tag,
        SortableUniqueId? since = null) =>
        ReadEventsByTagAsync(eventStore, tag, ResolveEventTypes(eventStore), since);

    public static async Task<ResultBox<IEnumerable<Event>>> ReadEventsByTagAsync(
        this IEventStore eventStore,
        ITag tag,
        IEventTypes eventTypes,
        SortableUniqueId? since = null)
    {
        var result = await eventStore.ReadSerializableEventsByTagAsync(tag, since);
        if (!result.IsSuccess)
        {
            return ResultBox.Error<IEnumerable<Event>>(result.GetException());
        }

        return DeserializeEvents(result.GetValue(), eventTypes);
    }

    public static Task<ResultBox<Event>> ReadEventAsync(
        this IEventStore eventStore,
        Guid eventId) =>
        ReadEventAsync(eventStore, eventId, ResolveEventTypes(eventStore));

    public static async Task<ResultBox<Event>> ReadEventAsync(
        this IEventStore eventStore,
        Guid eventId,
        IEventTypes eventTypes)
    {
        var result = await eventStore.ReadSerializableEventAsync(eventId);
        if (!result.IsSuccess)
        {
            return ResultBox.Error<Event>(result.GetException());
        }

        var eventResult = result.GetValue().ToEvent(eventTypes);
        return eventResult.IsSuccess
            ? eventResult
            : ResultBox.Error<Event>(eventResult.GetException());
    }

    public static Task<ResultBox<(IReadOnlyList<Event> Events, IReadOnlyList<TagWriteResult> TagWrites)>> WriteEventsAsync(
        this IEventStore eventStore,
        IEnumerable<Event> events) =>
        WriteEventsAsync(eventStore, events, ResolveEventTypes(eventStore));

    public static async Task<ResultBox<(IReadOnlyList<Event> Events, IReadOnlyList<TagWriteResult> TagWrites)>> WriteEventsAsync(
        this IEventStore eventStore,
        IEnumerable<Event> events,
        IEventTypes eventTypes)
    {
        var serializableEvents = events
            .Select(evt => evt.ToSerializableEvent(eventTypes))
            .ToList();
        var result = await eventStore.WriteSerializableEventsAsync(serializableEvents);
        if (!result.IsSuccess)
        {
            return ResultBox.Error<(IReadOnlyList<Event> Events, IReadOnlyList<TagWriteResult> TagWrites)>(result.GetException());
        }

        var writtenEventsResult = DeserializeEvents(result.GetValue().Events, eventTypes);
        if (!writtenEventsResult.IsSuccess)
        {
            return ResultBox.Error<(IReadOnlyList<Event> Events, IReadOnlyList<TagWriteResult> TagWrites)>(
                writtenEventsResult.GetException());
        }

        return ResultBox.FromValue((
            (IReadOnlyList<Event>)writtenEventsResult.GetValue().ToList(),
            result.GetValue().TagWrites));
    }

    public static Task<ResultBox<Event>> WriteEventAsync(
        this IEventStore eventStore,
        Event evt) =>
        WriteEventAsync(eventStore, evt, ResolveEventTypes(eventStore));

    /// <summary>
    ///     Helper method to write a single typed event through the serializable-only store interface.
    /// </summary>
    public static async Task<ResultBox<Event>> WriteEventAsync(
        this IEventStore eventStore,
        Event evt,
        IEventTypes eventTypes)
    {
        var result = await eventStore.WriteEventsAsync(new[] { evt }, eventTypes);
        if (!result.IsSuccess)
        {
            return ResultBox.Error<Event>(result.GetException());
        }

        return ResultBox.FromValue(result.GetValue().Events.First());
    }

    private static ResultBox<IEnumerable<Event>> DeserializeEvents(
        IEnumerable<SerializableEvent> events,
        IEventTypes eventTypes)
    {
        var materialized = new List<Event>();
        foreach (var serializableEvent in events)
        {
            var eventResult = serializableEvent.ToEvent(eventTypes);
            if (!eventResult.IsSuccess)
            {
                return ResultBox.Error<IEnumerable<Event>>(eventResult.GetException());
            }

            materialized.Add(eventResult.GetValue());
        }

        return ResultBox.FromValue<IEnumerable<Event>>(materialized);
    }

    private static IEventTypes ResolveEventTypes(IEventStore eventStore)
    {
        if (eventStore is InMemoryEventStore inMemoryEventStore && inMemoryEventStore.EventTypes is not null)
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
