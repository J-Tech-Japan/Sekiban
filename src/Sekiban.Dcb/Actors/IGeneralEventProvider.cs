using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
namespace Sekiban.Dcb.Actors;

/// <summary>
///     Interface for providing events to Multi Projection Actors
///     Manages both historical (safe) and live (unsafe) event streaming
/// </summary>
public interface IGeneralEventProvider : IDisposable
{

    /// <summary>
    ///     Get current state of the provider
    /// </summary>
    EventProviderState State { get; }

    /// <summary>
    ///     Get current position being processed
    /// </summary>
    SortableUniqueId? CurrentPosition { get; }

    /// <summary>
    ///     Whether the provider has caught up to live events
    /// </summary>
    bool IsCaughtUp { get; }
    /// <summary>
    ///     Start providing events to the callback
    /// </summary>
    /// <param name="onEvent">Callback when an event should be processed</param>
    /// <param name="fromPosition">Starting position (null for beginning)</param>
    /// <param name="eventTopic">Event topic to subscribe to (e.g., "event.all")</param>
    /// <param name="filter">Optional filter for events</param>
    /// <param name="batchSize">Number of events to process in each batch</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Provider handle for managing the stream</returns>
    Task<IEventProviderHandle> StartAsync(
        Func<Event, bool, Task> onEvent, // Event and isSafe flag
        SortableUniqueId? fromPosition = null,
        string eventTopic = "event.all",
        IEventProviderFilter? filter = null,
        int batchSize = 10000,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Start providing events to a multi-projection actor
    /// </summary>
    /// <param name="actor">Multi-projection actor to send events to</param>
    /// <param name="fromPosition">Starting position (null for beginning)</param>
    /// <param name="eventTopic">Event topic to subscribe to</param>
    /// <param name="filter">Optional filter for events</param>
    /// <param name="batchSize">Number of events to process in each batch</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Provider handle for managing the stream</returns>
    Task<IEventProviderHandle> StartWithActorAsync(
        IMultiProjectionActorCommon actor,
        SortableUniqueId? fromPosition = null,
        string eventTopic = "event.all",
        IEventProviderFilter? filter = null,
        int batchSize = 10000,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Start providing events with batch callback
    /// </summary>
    /// <param name="onEventBatch">Callback for batch of events</param>
    /// <param name="fromPosition">Starting position (null for beginning)</param>
    /// <param name="eventTopic">Event topic to subscribe to</param>
    /// <param name="filter">Optional filter for events</param>
    /// <param name="batchSize">Number of events to process in each batch</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Provider handle for managing the stream</returns>
    Task<IEventProviderHandle> StartWithBatchCallbackAsync(
        Func<IReadOnlyList<(Event evt, bool isSafe)>, Task> onEventBatch,
        SortableUniqueId? fromPosition = null,
        string eventTopic = "event.all",
        IEventProviderFilter? filter = null,
        int batchSize = 10000,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Get the safe window threshold
    /// </summary>
    SortableUniqueId GetSafeWindowThreshold();
}
