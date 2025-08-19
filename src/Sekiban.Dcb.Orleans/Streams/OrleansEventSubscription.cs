using Orleans.Streams;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Events;
using System.Collections.Concurrent;

namespace Sekiban.Dcb.Orleans.Streams;

/// <summary>
/// Orleans implementation of IEventSubscription for subscribing to event streams
/// Can be used inside actors or outside for read models, SignalR connections, etc.
/// </summary>
public class OrleansEventSubscription : IEventSubscription
{
    private readonly IClusterClient _clusterClient;
    private readonly string _providerName;
    private readonly string _streamNamespace;
    private readonly Guid _streamId;
    private readonly ConcurrentDictionary<string, OrleansEventSubscriptionHandleEnhanced> _subscriptions;
    private bool _disposed;

    /// <summary>
    /// Initialize Orleans event subscription
    /// </summary>
    /// <param name="clusterClient">Orleans cluster client</param>
    /// <param name="providerName">Stream provider name (e.g., "EventStream")</param>
    /// <param name="streamNamespace">Stream namespace (e.g., "Sekiban.Events")</param>
    /// <param name="streamId">Optional stream ID (defaults to new Guid if not provided)</param>
    public OrleansEventSubscription(
        IClusterClient clusterClient,
        string providerName,
        string streamNamespace,
        Guid? streamId = null)
    {
        _clusterClient = clusterClient ?? throw new ArgumentNullException(nameof(clusterClient));
        _providerName = providerName ?? throw new ArgumentNullException(nameof(providerName));
        _streamNamespace = streamNamespace ?? throw new ArgumentNullException(nameof(streamNamespace));
        _streamId = streamId ?? Guid.NewGuid();
        _subscriptions = new ConcurrentDictionary<string, OrleansEventSubscriptionHandleEnhanced>();
    }

    /// <summary>
    /// Subscribe to all events from the beginning
    /// </summary>
    public async Task<IEventSubscriptionHandle> SubscribeAsync(
        Func<Event, Task> onEventReceived,
        string? subscriptionId = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        subscriptionId ??= Guid.NewGuid().ToString();
        
        // Get the Orleans stream
        var streamProvider = _clusterClient.GetStreamProvider(_providerName);
        var stream = streamProvider.GetStream<Event>(_streamNamespace, _streamId);
        
        // Create subscription handle
        var handle = new OrleansEventSubscriptionHandleEnhanced(
            subscriptionId,
            stream,
            onEventReceived,
            filter: null,
            onDispose: () => _subscriptions.TryRemove(subscriptionId, out _));
        
        // Subscribe to the stream
        await handle.SubscribeAsync(cancellationToken);
        
        // Track the subscription
        _subscriptions[subscriptionId] = handle;
        
        return handle;
    }

    /// <summary>
    /// Subscribe to events starting from a specific position
    /// </summary>
    public async Task<IEventSubscriptionHandle> SubscribeFromAsync(
        string fromPosition,
        Func<Event, Task> onEventReceived,
        string? subscriptionId = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        subscriptionId ??= Guid.NewGuid().ToString();
        
        // Get the Orleans stream
        var streamProvider = _clusterClient.GetStreamProvider(_providerName);
        var stream = streamProvider.GetStream<Event>(_streamNamespace, _streamId);
        
        // Create subscription handle with starting position
        var handle = new OrleansEventSubscriptionHandleEnhanced(
            subscriptionId,
            stream,
            onEventReceived,
            filter: null,
            startingPosition: fromPosition,
            onDispose: () => _subscriptions.TryRemove(subscriptionId, out _));
        
        // Subscribe to the stream from the specified position
        await handle.SubscribeFromAsync(fromPosition, cancellationToken);
        
        // Track the subscription
        _subscriptions[subscriptionId] = handle;
        
        return handle;
    }

    /// <summary>
    /// Subscribe to events with filtering
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
        
        // Get the Orleans stream
        var streamProvider = _clusterClient.GetStreamProvider(_providerName);
        var stream = streamProvider.GetStream<Event>(_streamNamespace, _streamId);
        
        // Create subscription handle with filter
        var handle = new OrleansEventSubscriptionHandleEnhanced(
            subscriptionId,
            stream,
            onEventReceived,
            filter,
            onDispose: () => _subscriptions.TryRemove(subscriptionId, out _));
        
        // Subscribe to the stream with filter
        await handle.SubscribeAsync(cancellationToken);
        
        // Track the subscription
        _subscriptions[subscriptionId] = handle;
        
        return handle;
    }

    /// <summary>
    /// Get status of all active subscriptions
    /// </summary>
    public IEnumerable<IEventSubscriptionStatus> GetAllSubscriptionStatuses()
    {
        return _subscriptions.Values
            .Where(s => s != null)
            .Select(s => s.GetStatus())
            .ToList();
    }
    
    /// <summary>
    /// Get status of a specific subscription
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
    /// Dispose all subscriptions
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