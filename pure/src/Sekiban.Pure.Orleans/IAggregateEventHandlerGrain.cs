using Sekiban.Pure.Events;
namespace Sekiban.Pure.Orleans;

public interface IAggregateEventHandlerGrain : IGrainWithStringKey
{
    /// <summary>
    ///     Save new events based on the specified lastSortableUniqueId.
    ///     Performs optimistic locking and returns an error or diff if the LastSortableUniqueId differs from the local one.
    ///     Returns the new LastSortableUniqueId on success.
    /// </summary>
    /// <param name="expectedLastSortableUniqueId">The last SortableUniqueId recognized by the Projector</param>
    /// <param name="newEvents">List of created events</param>
    /// <returns>
    ///     (On success) New LastSortableUniqueId
    ///     (On failure) Throws an exception or returns diff events in another pattern
    /// </returns>
    Task<IReadOnlyList<IEvent>> AppendEventsAsync(string expectedLastSortableUniqueId, IReadOnlyList<IEvent> newEvents);

    /// <summary>
    ///     Get event diffs.
    /// </summary>
    /// <param name="fromSortableUniqueId">SortableUniqueId as the starting point for getting diffs</param>
    /// <param name="limit">Maximum number to retrieve (if needed)</param>
    /// <returns>List of matching events</returns>
    Task<IReadOnlyList<IEvent>> GetDeltaEventsAsync(string fromSortableUniqueId, int? limit = null);

    /// <summary>
    ///     Get all events from the beginning.
    ///     Used when recreating State due to Projector version changes, etc.
    ///     Consider paging when the number of items is large.
    /// </summary>
    /// <returns>List of all events</returns>
    Task<IReadOnlyList<IEvent>> GetAllEventsAsync();

    /// <summary>
    ///     Returns the currently managed last SortableUniqueId.
    /// </summary>
    /// <returns>Last SortableUniqueId</returns>
    Task<string> GetLastSortableUniqueIdAsync();

    /// <summary>
    ///     Register the specified projector (optional).
    ///     When there are multiple Projectors, consider a mechanism to record the last retrieval position for diffs.
    /// </summary>
    /// <param name="projectorKey">Unique key of the projector</param>
    /// <returns></returns>
    Task RegisterProjectorAsync(string projectorKey);
}
