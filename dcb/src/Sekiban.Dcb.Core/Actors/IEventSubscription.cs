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

    /// <summary>
    ///     Get status of all active subscriptions
    /// </summary>
    /// <returns>Collection of subscription statuses</returns>
    IEnumerable<IEventSubscriptionStatus> GetAllSubscriptionStatuses();

    /// <summary>
    ///     Get status of a specific subscription
    /// </summary>
    /// <param name="subscriptionId">Subscription ID</param>
    /// <returns>Subscription status if found, null otherwise</returns>
    IEventSubscriptionStatus? GetSubscriptionStatus(string subscriptionId);
}
