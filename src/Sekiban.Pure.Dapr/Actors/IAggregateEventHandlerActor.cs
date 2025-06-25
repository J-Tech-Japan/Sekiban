using Dapr.Actors;

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
    /// <param name="newEventEnvelopes">The new event envelopes to append</param>
    /// <returns>Response indicating success or failure</returns>
    Task<EventHandlingResponse> AppendEventsAsync(string expectedLastSortableUniqueId, List<EventEnvelope> newEventEnvelopes);

    /// <summary>
    /// Gets delta events from a specific point in the stream.
    /// </summary>
    /// <param name="fromSortableUniqueId">The starting point for retrieving events</param>
    /// <param name="limit">Limit on the number of events to retrieve (use -1 for no limit)</param>
    /// <returns>List of event envelopes after the specified point</returns>
    Task<List<EventEnvelope>> GetDeltaEventsAsync(string fromSortableUniqueId, int limit);

    /// <summary>
    /// Gets all events for the aggregate stream.
    /// </summary>
    /// <returns>All event envelopes in the stream</returns>
    Task<List<EventEnvelope>> GetAllEventsAsync();

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