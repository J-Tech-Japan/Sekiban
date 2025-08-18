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

/// <summary>
///     Handle for managing an event feeder
/// </summary>
public interface IEventFeederHandle : IDisposable
{
    /// <summary>
    ///     Unique ID for this feeder
    /// </summary>
    string FeederId { get; }

    /// <summary>
    ///     Current state of the feeder
    /// </summary>
    EventFeederState State { get; }

    /// <summary>
    ///     Current position being processed
    /// </summary>
    string? CurrentPosition { get; }

    /// <summary>
    ///     Number of events processed
    /// </summary>
    long EventsProcessed { get; }

    /// <summary>
    ///     Whether the feeder is catching up on historical events
    /// </summary>
    bool IsCatchingUp { get; }

    /// <summary>
    ///     Pause the feeder
    /// </summary>
    Task PauseAsync();

    /// <summary>
    ///     Resume the feeder
    /// </summary>
    Task ResumeAsync();

    /// <summary>
    ///     Stop the feeder and clean up resources
    /// </summary>
    Task StopAsync();

    /// <summary>
    ///     Wait for the feeder to catch up to real-time
    /// </summary>
    /// <param name="timeout">Maximum time to wait</param>
    /// <returns>True if caught up, false if timeout</returns>
    Task<bool> WaitForCatchUpAsync(TimeSpan timeout);
}

/// <summary>
///     State of the event feeder
/// </summary>
public enum EventFeederState
{
    /// <summary>
    ///     Not started
    /// </summary>
    NotStarted,

    /// <summary>
    ///     Catching up on historical events
    /// </summary>
    CatchingUp,

    /// <summary>
    ///     Processing live events
    /// </summary>
    Live,

    /// <summary>
    ///     Paused
    /// </summary>
    Paused,

    /// <summary>
    ///     Stopped
    /// </summary>
    Stopped,

    /// <summary>
    ///     Error state
    /// </summary>
    Error
}