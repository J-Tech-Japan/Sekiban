using Orleans.Streams;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Events;
using System.Collections.Concurrent;
namespace Sekiban.Dcb.Orleans.Streams;

/// <summary>
///     Orleans implementation of IEventSubscription for subscribing to event streams
///     Can be used inside actors or outside for read models, SignalR connections, etc.
/// </summary>
public class OrleansEventSubscription : IEventSubscription
{
    private readonly IAsyncStream<SerializableEvent> _stream;
    private readonly Guid _streamId;
    private readonly string _streamNamespace;
    private readonly IStreamProvider _streamProvider;
    private readonly DcbDomainTypes _domainTypes;
    private readonly ConcurrentDictionary<string, IEventSubscriptionHandle> _subscriptions;
    private bool _disposed;

    /// <summary>
    ///     Initialize Orleans event subscription
    /// </summary>
    /// <param name="streamProvider">Orleans stream provider</param>
    /// <param name="streamNamespace">Stream namespace (e.g., "Sekiban.Events")</param>
    /// <param name="domainTypes">Domain types for event serialization</param>
    /// <param name="streamId">Optional stream ID (defaults to new Guid if not provided)</param>
    public OrleansEventSubscription(
        IStreamProvider streamProvider,
        string streamNamespace,
        DcbDomainTypes domainTypes,
        Guid? streamId = null)
    {
        _streamProvider = streamProvider ?? throw new ArgumentNullException(nameof(streamProvider));
        _streamNamespace = streamNamespace ?? throw new ArgumentNullException(nameof(streamNamespace));
        _domainTypes = domainTypes ?? throw new ArgumentNullException(nameof(domainTypes));
        _streamId = streamId ?? Guid.Empty;
        _subscriptions = new ConcurrentDictionary<string, IEventSubscriptionHandle>();

        // Get the stream once during initialization
        _stream = _streamProvider.GetStream<SerializableEvent>(StreamId.Create(_streamNamespace, _streamId));
    }

    /// <summary>
    ///     Subscribe to all events from the beginning
    /// </summary>
    public async Task<IEventSubscriptionHandle> SubscribeAsync(
        Func<Event, Task> onEventReceived,
        string? subscriptionId = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        subscriptionId ??= Guid.NewGuid().ToString();

        // Create an observer for the Orleans stream
        var observer = new EventStreamObserver(subscriptionId, onEventReceived, _domainTypes);

        // Subscribe to the Orleans stream
        var orleansHandle = await _stream.SubscribeAsync(observer, null);

        // Create our handle wrapper
        var handle = new OrleansEventSubscriptionHandleSimple(
            subscriptionId,
            orleansHandle,
            () => _subscriptions.TryRemove(subscriptionId, out _));

        // Track the subscription
        _subscriptions[subscriptionId] = handle;

        return handle;
    }

    /// <summary>
    ///     Subscribe to events starting from a specific position
    /// </summary>
    public async Task<IEventSubscriptionHandle> SubscribeFromAsync(
        string fromPosition,
        Func<Event, Task> onEventReceived,
        string? subscriptionId = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        subscriptionId ??= Guid.NewGuid().ToString();

        // Create an observer for the Orleans stream
        var observer = new EventStreamObserver(subscriptionId, onEventReceived, _domainTypes);

        // Note: Orleans streams don't support replaying from a specific position by default
        // This would require custom implementation with persistent checkpoints
        // For now, subscribe from current position
        var orleansHandle = await _stream.SubscribeAsync(observer, null);

        // Create our handle wrapper
        var handle = new OrleansEventSubscriptionHandleSimple(
            subscriptionId,
            orleansHandle,
            () => _subscriptions.TryRemove(subscriptionId, out _));

        handle.UpdatePosition(fromPosition); // Store the position even though we can't replay from it

        // Track the subscription
        _subscriptions[subscriptionId] = handle;

        return handle;
    }

    /// <summary>
    ///     Subscribe to events with filtering
    /// </summary>
    public async Task<IEventSubscriptionHandle> SubscribeWithFilterAsync(
        IEventFilter filter,
        Func<Event, Task> onEventReceived,
        string? subscriptionId = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (filter == null)
            throw new ArgumentNullException(nameof(filter));

        subscriptionId ??= Guid.NewGuid().ToString();

        // Create an observer for the Orleans stream with filter
        var observer = new EventStreamObserver(subscriptionId, onEventReceived, _domainTypes, filter);

        // Subscribe to the Orleans stream
        var orleansHandle = await _stream.SubscribeAsync(observer, null);

        // Create our handle wrapper
        var handle = new OrleansEventSubscriptionHandleSimple(
            subscriptionId,
            orleansHandle,
            () => _subscriptions.TryRemove(subscriptionId, out _));

        // Track the subscription
        _subscriptions[subscriptionId] = handle;

        return handle;
    }

    /// <summary>
    ///     Get status of all active subscriptions
    /// </summary>
    public IEnumerable<IEventSubscriptionStatus> GetAllSubscriptionStatuses()
    {
        return _subscriptions.Values.Where(s => s != null).Select(s => s.GetStatus()).ToList();
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

        // Dispose all active subscriptions
        foreach (var subscription in _subscriptions.Values)
        {
            subscription.Dispose();
        }

        _subscriptions.Clear();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(OrleansEventSubscription));
    }
}
