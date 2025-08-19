using Sekiban.Dcb.Events;

namespace Sekiban.Dcb.Actors;

/// <summary>
///     Interface for feeding events to multi-projections
///     Combines event subscription with event retrieval from IEventStore
/// </summary>
public interface IMultiProjectionEventFeeder
{
    /// <summary>
    ///     Start feeding events to the callback
    ///     This will retrieve historical events from IEventStore and then subscribe to new events
    /// </summary>
    /// <param name="fromPosition">Starting position (null for beginning)</param>
    /// <param name="onEventBatch">Callback for processing event batches</param>
    /// <param name="batchSize">Number of events to process in each batch</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Feeder handle for managing the feed</returns>
    Task<IEventFeederHandle> StartFeedingAsync(
        string? fromPosition,
        Func<IReadOnlyList<Event>, Task> onEventBatch,
        int batchSize = 100,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Start feeding events with filtering
    /// </summary>
    /// <param name="filter">Event filter</param>
    /// <param name="fromPosition">Starting position (null for beginning)</param>
    /// <param name="onEventBatch">Callback for processing event batches</param>
    /// <param name="batchSize">Number of events to process in each batch</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Feeder handle</returns>
    Task<IEventFeederHandle> StartFeedingWithFilterAsync(
        IEventFilter filter,
        string? fromPosition,
        Func<IReadOnlyList<Event>, Task> onEventBatch,
        int batchSize = 100,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Get events in a specific range (for catch-up reads)
    /// </summary>
    /// <param name="fromPosition">Start position (inclusive)</param>
    /// <param name="toPosition">End position (inclusive)</param>
    /// <param name="batchSize">Batch size for retrieval</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Stream of events</returns>
    IAsyncEnumerable<Event> GetEventsInRangeAsync(
        string fromPosition,
        string toPosition,
        int batchSize = 100,
        CancellationToken cancellationToken = default);
}