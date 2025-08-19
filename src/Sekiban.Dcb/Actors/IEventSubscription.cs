using Sekiban.Dcb.Events;

namespace Sekiban.Dcb.Actors;

/// <summary>
///     Interface for subscribing to events from various sources
/// </summary>
public interface IEventSubscription : IDisposable
{
    /// <summary>
    ///     Subscribe to events with a callback
    /// </summary>
    /// <param name="onEventReceived">Callback when an event is received</param>
    /// <param name="subscriptionId">Optional subscription ID for tracking</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Subscription handle that can be disposed to unsubscribe</returns>
    Task<IEventSubscriptionHandle> SubscribeAsync(
        Func<Event, Task> onEventReceived,
        string? subscriptionId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Subscribe to events starting from a specific position
    /// </summary>
    /// <param name="fromPosition">Starting position (sortable unique ID or checkpoint)</param>
    /// <param name="onEventReceived">Callback when an event is received</param>
    /// <param name="subscriptionId">Optional subscription ID for tracking</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Subscription handle</returns>
    Task<IEventSubscriptionHandle> SubscribeFromAsync(
        string fromPosition,
        Func<Event, Task> onEventReceived,
        string? subscriptionId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Subscribe to events with filtering
    /// </summary>
    /// <param name="filter">Event filter</param>
    /// <param name="onEventReceived">Callback when an event is received</param>
    /// <param name="subscriptionId">Optional subscription ID for tracking</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Subscription handle</returns>
    Task<IEventSubscriptionHandle> SubscribeWithFilterAsync(
        IEventFilter filter,
        Func<Event, Task> onEventReceived,
        string? subscriptionId = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
///     Handle for managing an event subscription
/// </summary>
public interface IEventSubscriptionHandle : IDisposable
{
    /// <summary>
    ///     Unique ID for this subscription
    /// </summary>
    string SubscriptionId { get; }

    /// <summary>
    ///     Whether the subscription is currently active
    /// </summary>
    bool IsActive { get; }

    /// <summary>
    ///     Get the current position/checkpoint of the subscription
    /// </summary>
    string? CurrentPosition { get; }

    /// <summary>
    ///     Pause the subscription
    /// </summary>
    Task PauseAsync();

    /// <summary>
    ///     Resume the subscription
    /// </summary>
    Task ResumeAsync();

    /// <summary>
    ///     Unsubscribe and clean up resources
    /// </summary>
    Task UnsubscribeAsync();
}

/// <summary>
///     Filter for events
/// </summary>
public interface IEventFilter
{
    /// <summary>
    ///     Check if an event should be included
    /// </summary>
    bool ShouldInclude(Event evt);
}

/// <summary>
///     Filter events by type
/// </summary>
public record EventTypeFilter(HashSet<string> EventTypes) : IEventFilter
{
    public bool ShouldInclude(Event evt) => EventTypes.Contains(evt.EventType);
}

/// <summary>
///     Filter events by tags
/// </summary>
public record EventTagFilter(HashSet<string> RequiredTags) : IEventFilter
{
    public bool ShouldInclude(Event evt) => RequiredTags.All(tag => evt.Tags.Contains(tag));
}

/// <summary>
///     Composite filter that combines multiple filters with AND logic
/// </summary>
public record CompositeEventFilter(List<IEventFilter> Filters) : IEventFilter
{
    public bool ShouldInclude(Event evt) => Filters.All(f => f.ShouldInclude(evt));
}