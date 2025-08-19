using Orleans.Streams;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Events;
using System.Collections.Concurrent;

namespace Sekiban.Dcb.Orleans.Streams;

/// <summary>
/// Orleans event subscription with checkpoint support for durable subscriptions
/// </summary>
public class OrleansEventSubscriptionWithCheckpoint : IEventSubscription
{
    private readonly IClusterClient _clusterClient;
    private readonly ICheckpointManager _checkpointManager;
    private readonly string _providerName;
    private readonly string _streamNamespace;
    private readonly Guid _streamId;
    private readonly ConcurrentDictionary<string, OrleansEventSubscriptionHandleWithCheckpoint> _subscriptions;
    private bool _disposed;

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
    /// Initialize Orleans event subscription with checkpoint support
    /// </summary>
    public OrleansEventSubscriptionWithCheckpoint(
        IClusterClient clusterClient,
        ICheckpointManager checkpointManager,
        string providerName,
        string streamNamespace,
        Guid? streamId = null)
    {
        _clusterClient = clusterClient ?? throw new ArgumentNullException(nameof(clusterClient));
        _checkpointManager = checkpointManager ?? throw new ArgumentNullException(nameof(checkpointManager));
        _providerName = providerName ?? throw new ArgumentNullException(nameof(providerName));
        _streamNamespace = streamNamespace ?? throw new ArgumentNullException(nameof(streamNamespace));
        _streamId = streamId ?? Guid.NewGuid();
        _subscriptions = new ConcurrentDictionary<string, OrleansEventSubscriptionHandleWithCheckpoint>();
    }

    /// <summary>
    /// Subscribe to events, resuming from last checkpoint if available
    /// </summary>
    public async Task<IEventSubscriptionHandle> SubscribeAsync(
        Func<Event, Task> onEventReceived,
        string? subscriptionId = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        subscriptionId ??= Guid.NewGuid().ToString();
        
        // Check for existing checkpoint
        var checkpoint = await _checkpointManager.LoadCheckpointAsync(subscriptionId);
        
        if (checkpoint != null)
        {
            // Resume from checkpoint
            return await SubscribeFromAsync(checkpoint.Position, onEventReceived, subscriptionId, cancellationToken);
        }
        
        // Start from beginning
        var streamProvider = _clusterClient.GetStreamProvider(_providerName);
        var stream = streamProvider.GetStream<Event>(_streamNamespace, _streamId);
        
        var handle = new OrleansEventSubscriptionHandleWithCheckpoint(
            subscriptionId,
            stream,
            onEventReceived,
            _checkpointManager,
            filter: null,
            onDispose: () => _subscriptions.TryRemove(subscriptionId, out _));
        
        await handle.SubscribeAsync(cancellationToken);
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
        
        var streamProvider = _clusterClient.GetStreamProvider(_providerName);
        var stream = streamProvider.GetStream<Event>(_streamNamespace, _streamId);
        
        var handle = new OrleansEventSubscriptionHandleWithCheckpoint(
            subscriptionId,
            stream,
            onEventReceived,
            _checkpointManager,
            filter: null,
            startingPosition: fromPosition,
            onDispose: () => _subscriptions.TryRemove(subscriptionId, out _));
        
        await handle.SubscribeFromAsync(fromPosition, cancellationToken);
        _subscriptions[subscriptionId] = handle;
        
        return handle;
    }

    /// <summary>
    /// Subscribe to events with filtering, resuming from last checkpoint if available
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
        
        // Check for existing checkpoint
        var checkpoint = await _checkpointManager.LoadCheckpointAsync(subscriptionId);
        
        var streamProvider = _clusterClient.GetStreamProvider(_providerName);
        var stream = streamProvider.GetStream<Event>(_streamNamespace, _streamId);
        
        var handle = new OrleansEventSubscriptionHandleWithCheckpoint(
            subscriptionId,
            stream,
            onEventReceived,
            _checkpointManager,
            filter,
            startingPosition: checkpoint?.Position,
            onDispose: () => _subscriptions.TryRemove(subscriptionId, out _));
        
        if (checkpoint != null)
        {
            await handle.SubscribeFromAsync(checkpoint.Position, cancellationToken);
        }
        else
        {
            await handle.SubscribeAsync(cancellationToken);
        }
        
        _subscriptions[subscriptionId] = handle;
        
        return handle;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        
        _disposed = true;
        
        foreach (var subscription in _subscriptions.Values)
        {
            subscription.Dispose();
        }
        
        _subscriptions.Clear();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(OrleansEventSubscriptionWithCheckpoint));
    }
}

/// <summary>
/// Orleans event subscription handle with checkpoint support
/// </summary>
public class OrleansEventSubscriptionHandleWithCheckpoint : OrleansEventSubscriptionHandle
{
    private readonly ICheckpointManager _checkpointManager;
    private readonly TimeSpan _checkpointInterval;
    private DateTime _lastCheckpointTime;
    private readonly SemaphoreSlim _checkpointSemaphore = new(1, 1);

    public OrleansEventSubscriptionHandleWithCheckpoint(
        string subscriptionId,
        IAsyncStream<Event> stream,
        Func<Event, Task> onEventReceived,
        ICheckpointManager checkpointManager,
        IEventFilter? filter = null,
        string? startingPosition = null,
        TimeSpan? checkpointInterval = null,
        Action? onDispose = null)
        : base(subscriptionId, stream, WrapWithCheckpointing(onEventReceived, subscriptionId, checkpointManager, checkpointInterval), filter, startingPosition, onDispose)
    {
        _checkpointManager = checkpointManager ?? throw new ArgumentNullException(nameof(checkpointManager));
        _checkpointInterval = checkpointInterval ?? TimeSpan.FromSeconds(10);
        _lastCheckpointTime = DateTime.UtcNow;
    }

    private static Func<Event, Task> WrapWithCheckpointing(
        Func<Event, Task> originalHandler,
        string subscriptionId,
        ICheckpointManager checkpointManager,
        TimeSpan? checkpointInterval)
    {
        var interval = checkpointInterval ?? TimeSpan.FromSeconds(10);
        var lastCheckpointTime = DateTime.UtcNow;
        
        return async (Event evt) =>
        {
            // Process the event
            await originalHandler(evt);
            
            // Check if we should checkpoint
            var now = DateTime.UtcNow;
            if (now - lastCheckpointTime >= interval)
            {
                await checkpointManager.SaveCheckpointAsync(
                    subscriptionId,
                    evt.SortableUniqueIdValue,
                    new Dictionary<string, string>
                    {
                        ["EventId"] = evt.Id.ToString(),
                        ["EventType"] = evt.EventType,
                        ["Timestamp"] = now.ToString("O")
                    });
                
                lastCheckpointTime = now;
            }
        };
    }

    /// <summary>
    /// Force a checkpoint at the current position
    /// </summary>
    public async Task CheckpointAsync()
    {
        await _checkpointSemaphore.WaitAsync();
        try
        {
            if (!string.IsNullOrEmpty(CurrentPosition))
            {
                await _checkpointManager.SaveCheckpointAsync(
                    SubscriptionId,
                    CurrentPosition,
                    new Dictionary<string, string>
                    {
                        ["ForcedCheckpoint"] = "true",
                        ["Timestamp"] = DateTime.UtcNow.ToString("O")
                    });
                
                _lastCheckpointTime = DateTime.UtcNow;
            }
        }
        finally
        {
            _checkpointSemaphore.Release();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _checkpointSemaphore?.Dispose();
        }
        base.Dispose(disposing);
    }
}