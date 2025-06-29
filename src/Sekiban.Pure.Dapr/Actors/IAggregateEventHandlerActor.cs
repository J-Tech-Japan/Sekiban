using Dapr.Actors;
using Sekiban.Pure.Events;

namespace Sekiban.Pure.Dapr.Actors;

/// <summary>
/// Dapr actor interface for handling event persistence and retrieval for aggregate streams.
/// This is the Dapr equivalent of Orleans' IAggregateEventHandlerGrain.
/// Uses concrete types for proper JSON serialization by Dapr.
/// </summary>
public interface IAggregateEventHandlerActor : IActor
{
    /// <summary>
    /// Appends new events to the aggregate stream with optimistic concurrency control.
    /// </summary>
    /// <param name="expectedLastSortableUniqueId">The last sortable unique ID expected by the caller</param>
    /// <param name="newEventDocuments">The new serializable event documents to append</param>
    /// <returns>Response indicating success or failure</returns>
    Task<EventHandlingResponse> AppendEventsAsync(string expectedLastSortableUniqueId, List<SerializableEventDocument> newEventDocuments);

    /// <summary>
    /// Gets delta events from a specific point in the stream.
    /// </summary>
    /// <param name="fromSortableUniqueId">The starting point for retrieving events</param>
    /// <param name="limit">Limit on the number of events to retrieve (use -1 for no limit)</param>
    /// <returns>List of serializable event documents after the specified point</returns>
    Task<List<SerializableEventDocument>> GetDeltaEventsAsync(string fromSortableUniqueId, int limit);

    /// <summary>
    /// Gets all events for the aggregate stream.
    /// </summary>
    /// <returns>All serializable event documents in the stream</returns>
    Task<List<SerializableEventDocument>> GetAllEventsAsync();

    /// <summary>
    /// Gets the last sortable unique ID in the stream.
    /// </summary>
    /// <returns>The last sortable unique ID</returns>
    Task<string> GetLastSortableUniqueIdAsync();

    /// <summary>
    /// Registers a projector with this event handler (optional).
    /// </summary>
    /// <param name="projectorKey">The projector's unique key</param>
    /// <returns>Task</returns>
    Task RegisterProjectorAsync(string projectorKey);
}