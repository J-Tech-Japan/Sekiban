using Orleans.Streams;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Events;
using System.Collections.Concurrent;
namespace Sekiban.Dcb.Orleans.Streams;

/// <summary>
///     Orleans event subscription that uses a pre-existing stream subscription handle
///     This is used when the grain has already subscribed to the stream directly
/// </summary>
public class DirectOrleansEventSubscription : IEventSubscription
{
    private readonly StreamSubscriptionHandle<Event> _orleansHandle;
    private readonly IAsyncStream<Event> _stream;
    private readonly ConcurrentDictionary<string, DirectEventSubscriptionHandle> _subscriptions;
    private bool _disposed;

    /// <summary>
    ///     Initialize with an existing Orleans stream subscription
    /// </summary>
    public DirectOrleansEventSubscription(IAsyncStream<Event> stream, StreamSubscriptionHandle<Event> orleansHandle)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _orleansHandle = orleansHandle ?? throw new ArgumentNullException(nameof(orleansHandle));
        _subscriptions = new ConcurrentDictionary<string, DirectEventSubscriptionHandle>();

        Console.WriteLine("[DirectOrleansEventSubscription] Created with existing subscription handle");
    }

    /// <summary>
    ///     Subscribe using the existing Orleans handle
    ///     Note: This doesn't actually create a new subscription, it wraps the existing one
    /// </summary>
    public Task<IEventSubscriptionHandle> SubscribeAsync(
        Func<Event, Task> onEventReceived,
        string? subscriptionId = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        subscriptionId ??= Guid.NewGuid().ToString();

        Console.WriteLine($"[DirectOrleansEventSubscription] SubscribeAsync called for subscription {subscriptionId}");

        // Create a wrapper handle that doesn't actually control the subscription
        // The real subscription is managed by the grain
        var handle = new DirectEventSubscriptionHandle(subscriptionId, _orleansHandle, onEventReceived);

        _subscriptions[subscriptionId] = handle;

        Console.WriteLine(
            $"[DirectOrleansEventSubscription] Subscription {subscriptionId} created (using existing Orleans handle)");

        return Task.FromResult<IEventSubscriptionHandle>(handle);
    }

    /// <summary>
    ///     Not supported - the grain already has a subscription
    /// </summary>
    public Task<IEventSubscriptionHandle> SubscribeFromAsync(
        string fromPosition,
        Func<Event, Task> onEventReceived,
        string? subscriptionId = null,
        CancellationToken cancellationToken = default) =>
        // Just use the regular subscribe since we can't control the position
        SubscribeAsync(onEventReceived, subscriptionId, cancellationToken);

    /// <summary>
    ///     Not supported - the grain already has a subscription
    /// </summary>
    public Task<IEventSubscriptionHandle> SubscribeWithFilterAsync(
        IEventFilter filter,
        Func<Event, Task> onEventReceived,
        string? subscriptionId = null,
        CancellationToken cancellationToken = default) =>
        // Just use the regular subscribe since filtering will be done in the handle
        SubscribeAsync(onEventReceived, subscriptionId, cancellationToken);

    /// <summary>
    ///     Get status of all active subscriptions
    /// </summary>
    public IEnumerable<IEventSubscriptionStatus> GetAllSubscriptionStatuses()
    {
        return _subscriptions
            .Values
            .Where(s => s != null)
            .Select(s => (IEventSubscriptionStatus)s.GetStatus())
            .ToList();
    }

    /// <summary>
    ///     Get status of a specific subscription
    /// </summary>
    public IEventSubscriptionStatus? GetSubscriptionStatus(string subscriptionId)
    {
        if (_subscriptions.TryGetValue(subscriptionId, out var handle))
        {
            return handle?.GetStatus();
        }
        return null;
    }

    /// <summary>
    ///     Dispose all subscriptions
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        // Note: We don't unsubscribe the Orleans handle here
        // That's managed by the grain
        _subscriptions.Clear();
    }

    /// <summary>
    ///     Process an event received from the Orleans stream
    ///     This is called by the grain's observer when events arrive
    /// </summary>
    public async Task ProcessEventFromStream(Event evt)
    {
        if (_disposed) return;

        // Forward the event to all registered handlers
        var tasks = _subscriptions.Values.Select(handle => handle.ProcessEventAsync(evt));
        await Task.WhenAll(tasks);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(DirectOrleansEventSubscription));
    }
}
