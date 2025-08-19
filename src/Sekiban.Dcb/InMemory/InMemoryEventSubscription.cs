using System.Collections.Concurrent;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Events;

namespace Sekiban.Dcb.InMemory;

/// <summary>
///     In-memory implementation of event subscription for testing and development
/// </summary>
public class InMemoryEventSubscription : IEventSubscription
{
    private readonly ConcurrentDictionary<string, InMemorySubscriptionHandle> _subscriptions = new();
    private readonly List<Event> _eventStore = new();
    private readonly object _eventStoreLock = new();
    private bool _disposed;

    /// <summary>
    ///     Publish an event to all active subscriptions
    /// </summary>
    public async Task PublishEventAsync(Event evt)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(InMemoryEventSubscription));

        // Add to event store
        lock (_eventStoreLock)
        {
            _eventStore.Add(evt);
        }

        // Notify all active subscriptions
        var tasks = _subscriptions.Values
            .Where(s => s.IsActive && !s.IsPaused)
            .Select(s => s.NotifyEventAsync(evt))
            .ToList();

        await Task.WhenAll(tasks);
    }

    /// <summary>
    ///     Get all stored events (for testing)
    /// </summary>
    public IReadOnlyList<Event> GetAllEvents()
    {
        lock (_eventStoreLock)
        {
            return _eventStore.ToList();
        }
    }

    public async Task<IEventSubscriptionHandle> SubscribeAsync(
        Func<Event, Task> onEventReceived,
        string? subscriptionId = null,
        CancellationToken cancellationToken = default)
    {
        return await SubscribeFromAsync(null, onEventReceived, subscriptionId, cancellationToken);
    }

    public async Task<IEventSubscriptionHandle> SubscribeFromAsync(
        string? fromPosition,
        Func<Event, Task> onEventReceived,
        string? subscriptionId = null,
        CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(InMemoryEventSubscription));

        subscriptionId ??= Guid.NewGuid().ToString();
        
        var handle = new InMemorySubscriptionHandle(
            subscriptionId,
            onEventReceived,
            null,
            fromPosition,
            () => _subscriptions.TryRemove(subscriptionId, out _));

        if (!_subscriptions.TryAdd(subscriptionId, handle))
        {
            throw new InvalidOperationException($"Subscription with ID {subscriptionId} already exists");
        }

        // Process historical events if needed
        await ProcessHistoricalEventsAsync(handle, fromPosition, cancellationToken);

        return handle;
    }

    public async Task<IEventSubscriptionHandle> SubscribeWithFilterAsync(
        IEventFilter filter,
        Func<Event, Task> onEventReceived,
        string? subscriptionId = null,
        CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(InMemoryEventSubscription));

        subscriptionId ??= Guid.NewGuid().ToString();
        
        var handle = new InMemorySubscriptionHandle(
            subscriptionId,
            onEventReceived,
            filter,
            null,
            () => _subscriptions.TryRemove(subscriptionId, out _));

        if (!_subscriptions.TryAdd(subscriptionId, handle))
        {
            throw new InvalidOperationException($"Subscription with ID {subscriptionId} already exists");
        }

        // Process all historical events with filter
        await ProcessHistoricalEventsAsync(handle, null, cancellationToken);

        return handle;
    }

    private async Task ProcessHistoricalEventsAsync(
        InMemorySubscriptionHandle handle,
        string? fromPosition,
        CancellationToken cancellationToken)
    {
        List<Event> historicalEvents;
        lock (_eventStoreLock)
        {
            historicalEvents = _eventStore.ToList();
        }

        // Filter by position if specified
        if (!string.IsNullOrEmpty(fromPosition))
        {
            historicalEvents = historicalEvents
                .Where(e => string.Compare(e.SortableUniqueIdValue, fromPosition, StringComparison.Ordinal) > 0)
                .ToList();
        }

        // Process historical events
        foreach (var evt in historicalEvents)
        {
            if (cancellationToken.IsCancellationRequested) break;
            await handle.NotifyEventAsync(evt);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        // Dispose all subscriptions
        var handles = _subscriptions.Values.ToList();
        foreach (var handle in handles)
        {
            handle.Dispose();
        }

        _subscriptions.Clear();
        _disposed = true;
    }

    private class InMemorySubscriptionHandle : IEventSubscriptionHandle
    {
        private readonly Func<Event, Task> _onEventReceived;
        private readonly IEventFilter? _filter;
        private readonly Action _onDispose;
        private volatile bool _isActive = true;
        private volatile bool _isPaused;
        private string? _currentPosition;
        private bool _disposed;

        public InMemorySubscriptionHandle(
            string subscriptionId,
            Func<Event, Task> onEventReceived,
            IEventFilter? filter,
            string? initialPosition,
            Action onDispose)
        {
            SubscriptionId = subscriptionId;
            _onEventReceived = onEventReceived;
            _filter = filter;
            _currentPosition = initialPosition;
            _onDispose = onDispose;
        }

        public string SubscriptionId { get; }
        public bool IsActive => _isActive && !_disposed;
        public bool IsPaused => _isPaused;
        public string? CurrentPosition => _currentPosition;

        public async Task NotifyEventAsync(Event evt)
        {
            if (!IsActive || IsPaused) return;

            // Apply filter if present
            if (_filter != null && !_filter.ShouldInclude(evt))
            {
                return;
            }

            try
            {
                await _onEventReceived(evt);
                _currentPosition = evt.SortableUniqueIdValue;
            }
            catch
            {
                // Log error in production
                // For now, swallow to prevent breaking other subscriptions
            }
        }

        public Task PauseAsync()
        {
            _isPaused = true;
            return Task.CompletedTask;
        }

        public Task ResumeAsync()
        {
            _isPaused = false;
            return Task.CompletedTask;
        }

        public Task UnsubscribeAsync()
        {
            _isActive = false;
            Dispose();
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            if (_disposed) return;
            
            _isActive = false;
            _disposed = true;
            _onDispose?.Invoke();
        }
    }
}