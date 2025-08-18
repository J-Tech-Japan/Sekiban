using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.Actors;

/// <summary>
///     Interface for providing events to Multi Projection Actors
///     Manages both historical (safe) and live (unsafe) event streaming
/// </summary>
public interface IGeneralEventProvider : IDisposable
{
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
    ///     Get the safe window threshold
    /// </summary>
    SortableUniqueId GetSafeWindowThreshold();
}

/// <summary>
///     Handle for managing an event provider stream
/// </summary>
public interface IEventProviderHandle : IDisposable
{
    /// <summary>
    ///     Unique ID for this provider
    /// </summary>
    string ProviderId { get; }

    /// <summary>
    ///     Current state of the provider
    /// </summary>
    EventProviderState State { get; }

    /// <summary>
    ///     Pause event streaming
    /// </summary>
    Task PauseAsync();

    /// <summary>
    ///     Resume event streaming
    /// </summary>
    Task ResumeAsync();

    /// <summary>
    ///     Stop the provider and clean up resources (including subscription)
    /// </summary>
    Task StopAsync();

    /// <summary>
    ///     Stop the subscription but keep processing remaining events
    /// </summary>
    Task StopSubscriptionAsync();

    /// <summary>
    ///     Wait for the provider to catch up to live events
    /// </summary>
    Task<bool> WaitForCatchUpAsync(TimeSpan timeout);

    /// <summary>
    ///     Wait for current batch to complete
    /// </summary>
    Task WaitForCurrentBatchAsync();

    /// <summary>
    ///     Get statistics about events processed
    /// </summary>
    EventProviderStatistics GetStatistics();

    /// <summary>
    ///     Check if currently processing a batch
    /// </summary>
    bool IsProcessingBatch { get; }
}

/// <summary>
///     Filter for events
/// </summary>
public interface IEventProviderFilter
{
    /// <summary>
    ///     Check if an event should be included
    /// </summary>
    bool ShouldInclude(Event evt, List<ITag> tags);

    /// <summary>
    ///     Get tags to filter by (for EventStore queries)
    /// </summary>
    IEnumerable<ITag>? GetTagFilters();

    /// <summary>
    ///     Get event types to filter by
    /// </summary>
    IEnumerable<string>? GetEventTypeFilters();
}

/// <summary>
///     State of the event provider
/// </summary>
public enum EventProviderState
{
    NotStarted,
    CatchingUp,
    Live,
    Paused,
    Stopped,
    Error
}

/// <summary>
///     Statistics about events processed
/// </summary>
public record EventProviderStatistics(
    long TotalEventsProcessed,
    long SafeEventsProcessed,
    long UnsafeEventsProcessed,
    DateTime? LastEventTime,
    SortableUniqueId? LastEventPosition,
    TimeSpan? CatchUpDuration
);

/// <summary>
///     Filter events by tag group
/// </summary>
public record TagGroupFilter(string TagGroup) : IEventProviderFilter
{
    public bool ShouldInclude(Event evt, List<ITag> tags) =>
        tags.Any(t => t.GetTagGroup() == TagGroup);

    public IEnumerable<ITag>? GetTagFilters() => null; // Will be filtered in ShouldInclude
    public IEnumerable<string>? GetEventTypeFilters() => null;
}

/// <summary>
///     Filter events by specific tags
/// </summary>
public record SpecificTagsFilter(List<ITag> RequiredTags) : IEventProviderFilter
{
    public bool ShouldInclude(Event evt, List<ITag> tags) =>
        RequiredTags.All(required => tags.Any(t => t.Equals(required)));

    public IEnumerable<ITag> GetTagFilters() => RequiredTags;
    public IEnumerable<string>? GetEventTypeFilters() => null;
}

/// <summary>
///     Filter events by event types
/// </summary>
public record EventTypesFilter(HashSet<string> EventTypes) : IEventProviderFilter
{
    public bool ShouldInclude(Event evt, List<ITag> tags) =>
        EventTypes.Contains(evt.EventType);

    public IEnumerable<ITag>? GetTagFilters() => null;
    public IEnumerable<string> GetEventTypeFilters() => EventTypes;
}

/// <summary>
///     Composite filter combining multiple filters with AND logic
/// </summary>
public record CompositeProviderFilter(List<IEventProviderFilter> Filters) : IEventProviderFilter
{
    public bool ShouldInclude(Event evt, List<ITag> tags) =>
        Filters.All(f => f.ShouldInclude(evt, tags));

    public IEnumerable<ITag>? GetTagFilters() =>
        Filters.SelectMany(f => f.GetTagFilters() ?? Enumerable.Empty<ITag>()).Distinct();

    public IEnumerable<string>? GetEventTypeFilters() =>
        Filters.SelectMany(f => f.GetEventTypeFilters() ?? Enumerable.Empty<string>()).Distinct();
}