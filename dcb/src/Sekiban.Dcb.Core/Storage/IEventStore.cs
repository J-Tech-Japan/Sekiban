using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.Storage;

/// <summary>
///     Unified interface for event and tag storage operations. Failures are reported as exceptions; cancellation is
///     reported as <see cref="OperationCanceledException" />.
/// </summary>
public interface IEventStore
{
    // Tag operations

    /// <summary>
    ///     Reads tag streams for a specific tag
    /// </summary>
    Task<IEnumerable<TagStream>> ReadTagsAsync(ITag tag);

    /// <summary>
    ///     Gets the latest tag state. A tag with no events returns an empty <see cref="TagState" /> (version 0), not
    ///     an exception.
    /// </summary>
    Task<TagState> GetLatestTagAsync(ITag tag);

    /// <summary>
    ///     Checks if a tag exists
    /// </summary>
    Task<bool> TagExistsAsync(ITag tag);

    /// <summary>
    ///     Gets total event count, optionally after a position.
    /// </summary>
    Task<long> GetEventCountAsync(SortableUniqueId? since = null);

    /// <summary>
    ///     Gets all unique tags in the event store
    /// </summary>
    /// <param name="tagGroup">Optional: Filter by tag group name</param>
    /// <returns>List of unique tag information</returns>
    Task<IEnumerable<TagInfo>> GetAllTagsAsync(string? tagGroup = null);

    // SerializableEvent operations (payload stays serialized until an explicit caller-side conversion)

    /// <summary>
    ///     Reads all events as SerializableEvent (no payload deserialization).
    /// </summary>
    /// <param name="since">Optional: Only return events after this ID</param>
    Task<IEnumerable<SerializableEvent>> ReadAllSerializableEventsAsync(SortableUniqueId? since = null);

    /// <summary>
    ///     Reads all events as SerializableEvent (no payload deserialization).
    /// </summary>
    /// <param name="since">Optional: Only return events after this ID</param>
    /// <param name="maxCount">Optional: Maximum number of events to return</param>
    Task<IEnumerable<SerializableEvent>> ReadAllSerializableEventsAsync(
        SortableUniqueId? since,
        int? maxCount);

    /// <summary>
    ///     Reads a specific event as SerializableEvent. Returns <c>null</c> when no event with
    ///     <paramref name="eventId" /> exists.
    /// </summary>
    Task<SerializableEvent?> ReadSerializableEventAsync(Guid eventId);

    /// <summary>
    ///     Reads events for a specific tag as SerializableEvent (no payload deserialization).
    /// </summary>
    /// <param name="tag">The tag to filter events by</param>
    /// <param name="since">Optional: Only return events after this ID</param>
    Task<IEnumerable<SerializableEvent>> ReadSerializableEventsByTagAsync(ITag tag, SortableUniqueId? since = null);

    /// <summary>
    ///     Writes multiple pre-serialized events to the event store and updates tag states
    ///     for all affected tags. Atomicity of the event write, the tag write, and the
    ///     visibility of a multi-event batch is provider-dependent — see the
    ///     "Consistency Contract" section in docs/dcb_llm/11_storage_providers.md for the
    ///     guarantees of each storage provider.
    /// </summary>
    /// <returns>The written serializable events and tag write results. A failed write throws.</returns>
    Task<(IReadOnlyList<SerializableEvent> Events, IReadOnlyList<TagWriteResult> TagWrites)> WriteSerializableEventsAsync(
        IEnumerable<SerializableEvent> events);

    /// <summary>
    ///     Gets the latest (maximum) SortableUniqueId across all events in the store.
    ///     Returns empty string if no events exist.
    ///     This is a global operation not scoped to any tag or tag group.
    /// </summary>
    Task<string> GetLatestSortableUniqueIdAsync();
}

/// <summary>
///     Information about a tag in the event store
/// </summary>
public record TagInfo(
    string Tag,
    string TagGroup,
    int EventCount,
    string? FirstSortableUniqueId,
    string? LastSortableUniqueId,
    DateTime? FirstEventAt,
    DateTime? LastEventAt);
