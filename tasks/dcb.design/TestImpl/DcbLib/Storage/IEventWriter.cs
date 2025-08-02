using DcbLib.Common;
using DcbLib.Events;
using DcbLib.Tags;
using ResultBoxes;

namespace DcbLib.Storage;

/// <summary>
/// Writes events to the event store.
/// Called by CommandExecutor after successful reservation to persist events.
/// </summary>
public interface IEventWriter
{
    /// <summary>
    /// Writes a single event with its tags atomically
    /// </summary>
    /// <param name="eventPayload">The event payload to write</param>
    /// <param name="tags">Tags associated with this event</param>
    /// <param name="sortableUniqueId">The sortable unique ID for ordering</param>
    /// <param name="metadata">Optional event metadata</param>
    /// <returns>ResultBox containing the written Event or error</returns>
    Task<ResultBox<Event>> WriteEventAsync(
        IEventPayload eventPayload,
        IReadOnlyList<ITag> tags,
        SortableUniqueId sortableUniqueId,
        EventMetadata? metadata = null);

    /// <summary>
    /// Writes multiple events atomically (batch operation)
    /// </summary>
    /// <param name="eventWrites">Collection of event write operations</param>
    /// <returns>ResultBox containing list of written Events or error</returns>
    Task<ResultBox<IReadOnlyList<Event>>> WriteEventsAsync(
        IEnumerable<EventWriteOperation> eventWrites);

    /// <summary>
    /// Appends an event with conditional write based on expected position
    /// </summary>
    /// <param name="eventPayload">The event payload to write</param>
    /// <param name="tags">Tags associated with this event</param>
    /// <param name="sortableUniqueId">The sortable unique ID for ordering</param>
    /// <param name="expectedPosition">Expected global stream position for optimistic concurrency</param>
    /// <param name="metadata">Optional event metadata</param>
    /// <returns>ResultBox containing the written Event or concurrency error</returns>
    Task<ResultBox<Event>> AppendEventAsync(
        IEventPayload eventPayload,
        IReadOnlyList<ITag> tags,
        SortableUniqueId sortableUniqueId,
        long expectedPosition,
        EventMetadata? metadata = null);

    /// <summary>
    /// Gets the current global stream position
    /// </summary>
    /// <returns>The current position in the global event stream</returns>
    Task<long> GetGlobalPositionAsync();
}

/// <summary>
/// Represents a single event write operation for batch writes
/// </summary>
public record EventWriteOperation(
    IEventPayload EventPayload,
    IReadOnlyList<ITag> Tags,
    SortableUniqueId SortableUniqueId,
    EventMetadata? Metadata = null
);