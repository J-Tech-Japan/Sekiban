using ResultBoxes;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.Storage;

/// <summary>
///     Unified interface for event and tag storage operations
/// </summary>
public interface IEventStore
{
    // Tag operations

    /// <summary>
    ///     Reads tag streams for a specific tag
    /// </summary>
    Task<ResultBox<IEnumerable<TagStream>>> ReadTagsAsync(ITag tag);

    /// <summary>
    ///     Gets the latest tag state
    /// </summary>
    Task<ResultBox<TagState>> GetLatestTagAsync(ITag tag);

    /// <summary>
    ///     Checks if a tag exists
    /// </summary>
    Task<ResultBox<bool>> TagExistsAsync(ITag tag);

    /// <summary>
    ///     Gets total event count, optionally after a position.
    /// </summary>
    Task<ResultBox<long>> GetEventCountAsync(SortableUniqueId? since = null);

    /// <summary>
    ///     Gets all unique tags in the event store
    /// </summary>
    /// <param name="tagGroup">Optional: Filter by tag group name</param>
    /// <returns>List of unique tag information</returns>
    Task<ResultBox<IEnumerable<TagInfo>>> GetAllTagsAsync(string? tagGroup = null);

    // SerializableEvent operations (payload stays serialized until an explicit caller-side conversion)

    /// <summary>
    ///     Reads all events as SerializableEvent (no payload deserialization).
    /// </summary>
    /// <param name="since">Optional: Only return events after this ID</param>
    Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(SortableUniqueId? since = null);

    /// <summary>
    ///     Reads all events as SerializableEvent (no payload deserialization).
    /// </summary>
    /// <param name="since">Optional: Only return events after this ID</param>
    /// <param name="maxCount">Optional: Maximum number of events to return</param>
    Task<ResultBox<IEnumerable<SerializableEvent>>> ReadAllSerializableEventsAsync(
        SortableUniqueId? since,
        int? maxCount);

    /// <summary>
    ///     Reads a specific event as SerializableEvent.
    /// </summary>
    Task<ResultBox<SerializableEvent>> ReadSerializableEventAsync(Guid eventId);

    /// <summary>
    ///     Reads events for a specific tag as SerializableEvent (no payload deserialization).
    /// </summary>
    /// <param name="tag">The tag to filter events by</param>
    /// <param name="since">Optional: Only return events after this ID</param>
    Task<ResultBox<IEnumerable<SerializableEvent>>> ReadSerializableEventsByTagAsync(ITag tag, SortableUniqueId? since = null);

    /// <summary>
    ///     Writes multiple pre-serialized events to the event store atomically.
    ///     Also updates tag states for all affected tags.
    /// </summary>
    /// <returns>ResultBox containing the written serializable events and tag write results</returns>
    Task<ResultBox<(IReadOnlyList<SerializableEvent> Events, IReadOnlyList<TagWriteResult> TagWrites)>> WriteSerializableEventsAsync(
        IEnumerable<SerializableEvent> events);
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
