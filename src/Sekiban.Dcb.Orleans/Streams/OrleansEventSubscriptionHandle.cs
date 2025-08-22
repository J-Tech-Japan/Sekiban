using Orleans.Streams;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Events;

namespace Sekiban.Dcb.Orleans.Streams;

/// <summary>
/// Orleans implementation of IEventSubscriptionHandle for managing event stream subscriptions
/// </summary>
public class OrleansEventSubscriptionHandle : IEventSubscriptionHandle
{
    protected readonly IAsyncStream<Event> _stream;
    protected readonly Func<Event, Task> _onEventReceived;
    protected readonly IEventFilter? _filter;
    protected readonly Action? _onDispose;
    protected StreamSubscriptionHandle<Event>? _orleansHandle;
    protected string? _currentPosition;
    protected bool _isPaused;
    protected bool _disposed;
    protected readonly SemaphoreSlim _semaphore = new(1, 1);

    /// <summary>
    /// Unique ID for this subscription
    /// </summary>
    public string SubscriptionId { get; }

    /// <summary>
    /// Whether the subscription is currently active
    /// </summary>
    public bool IsActive => !_disposed && !_isPaused && _orleansHandle != null;

    /// <summary>
    /// Get the current position/checkpoint of the subscription
    /// </summary>
    public string? CurrentPosition => _currentPosition;

    public OrleansEventSubscriptionHandle(
        string subscriptionId,
        IAsyncStream<Event> stream,
        Func<Event, Task> onEventReceived,
        IEventFilter? filter = null,
        string? startingPosition = null,
        Action? onDispose = null)
    {
        SubscriptionId = subscriptionId ?? throw new ArgumentNullException(nameof(subscriptionId));
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _onEventReceived = onEventReceived ?? throw new ArgumentNullException(nameof(onEventReceived));
        _filter = filter;
        _currentPosition = startingPosition;
        _onDispose = onDispose;
    }

    /// <summary>
    /// Subscribe to the stream
    /// </summary>
    internal async Task SubscribeAsync(CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(OrleansEventSubscriptionHandle));

            if (_orleansHandle != null)
                return; // Already subscribed

            // Create the observer with optional filtering
            var observer = new EventObserver(HandleEventAsync);
            
            // Subscribe to the stream
            _orleansHandle = await _stream.SubscribeAsync(observer);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Subscribe to the stream from a specific position
    /// </summary>
    internal async Task SubscribeFromAsync(string fromPosition, CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(OrleansEventSubscriptionHandle));

            if (_orleansHandle != null)
                return; // Already subscribed

            // Create the observer with optional filtering
            var observer = new EventObserver(HandleEventAsync);
            
            // Try to parse the position as a sequence token
            // This is a simplified implementation - in production, you'd need proper token management
            StreamSequenceToken? token = null;
            if (!string.IsNullOrEmpty(fromPosition))
            {
                // In a real implementation, you would deserialize the token from the position string
                // For now, we'll subscribe from the beginning with a note that position-based
                // subscription requires proper token persistence
            }
            
            // Subscribe to the stream with the token
            _orleansHandle = await _stream.SubscribeAsync(observer, token);
            _currentPosition = fromPosition;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Handle incoming events
    /// </summary>
    private async Task HandleEventAsync(Event evt, StreamSequenceToken? token)
    {
        if (_disposed || _isPaused)
            return;

        // Apply filter if configured
        if (_filter != null && !_filter.ShouldInclude(evt))
            return;

        try
        {
            // Process the event
            await _onEventReceived(evt);
            
            // Update position based on the event's sortable unique ID
            _currentPosition = evt.SortableUniqueIdValue;
        }
        catch (Exception ex)
        {
            // Log the error but continue processing
            // In production, you might want to have error handling strategies
            Console.WriteLine($"Error processing event in subscription {SubscriptionId}: {ex.Message}");
        }
    }

    /// <summary>
    /// Pause the subscription
    /// </summary>
    public async Task PauseAsync()
    {
        await _semaphore.WaitAsync();
        try
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(OrleansEventSubscriptionHandle));

            _isPaused = true;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Resume the subscription
    /// </summary>
    public async Task ResumeAsync()
    {
        await _semaphore.WaitAsync();
        try
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(OrleansEventSubscriptionHandle));

            _isPaused = false;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Get detailed status information about the subscription
    /// </summary>
    public virtual IEventSubscriptionStatus GetStatus()
    {
        // Basic implementation - enhanced version provides more details
        return new OrleansEventSubscriptionStatus(
            SubscriptionId,
            IsActive,
            _isPaused,
            _currentPosition,
            startedAt: null,
            pausedAt: null,
            eventsReceived: 0,
            eventsProcessed: 0,
            errorCount: 0,
            lastError: null,
            lastErrorAt: null,
            lastEventReceivedAt: null,
            lastEventProcessedAt: null,
            averageProcessingTimeMs: null);
    }
    
    /// <summary>
    /// Unsubscribe and clean up resources
    /// </summary>
    public async Task UnsubscribeAsync()
    {
        await _semaphore.WaitAsync();
        try
        {
            if (_disposed)
                return;

            if (_orleansHandle != null)
            {
                await _orleansHandle.UnsubscribeAsync();
                _orleansHandle = null;
            }

            _disposed = true;
            _onDispose?.Invoke();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// Dispose the subscription handle
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Protected disposal method for inheritance
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        if (disposing)
        {
            // Synchronously wait for unsubscribe
            // In production, consider using DisposeAsync pattern
            UnsubscribeAsync().GetAwaiter().GetResult();
            _semaphore?.Dispose();
        }
    }

    /// <summary>
    /// Internal observer for handling stream events
    /// </summary>
    private class EventObserver : IAsyncObserver<Event>
    {
        private readonly Func<Event, StreamSequenceToken?, Task> _onNext;

        public EventObserver(Func<Event, StreamSequenceToken?, Task> onNext)
        {
            _onNext = onNext;
        }

        public async Task OnNextAsync(Event item, StreamSequenceToken? token = null)
        {
            await _onNext(item, token);
        }

        public Task OnCompletedAsync()
        {
            // Stream completed
            return Task.CompletedTask;
        }

        public Task OnErrorAsync(Exception ex)
        {
            // Log error
            Console.WriteLine($"Stream error: {ex.Message}");
            return Task.CompletedTask;
        }
    }
}