using Orleans.Streams;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Events;
using System.Diagnostics;

namespace Sekiban.Dcb.Orleans.Streams;

/// <summary>
/// Enhanced Orleans event subscription handle with statistics tracking
/// </summary>
public class OrleansEventSubscriptionHandleEnhanced : IEventSubscriptionHandle
{
    private readonly IAsyncStream<Event> _stream;
    private readonly Func<Event, Task> _onEventReceived;
    private readonly IEventFilter? _filter;
    private readonly Action? _onDispose;
    private StreamSubscriptionHandle<Event>? _orleansHandle;
    private string? _currentPosition;
    private bool _isPaused;
    private bool _disposed;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    
    // Statistics
    private DateTime? _startedAt;
    private DateTime? _pausedAt;
    private long _eventsReceived;
    private long _eventsProcessed;
    private long _errorCount;
    private string? _lastError;
    private DateTime? _lastErrorAt;
    private DateTime? _lastEventReceivedAt;
    private DateTime? _lastEventProcessedAt;
    private readonly List<double> _processingTimes = new();
    private readonly object _statsLock = new();

    public string SubscriptionId { get; }
    public bool IsActive => !_disposed && !_isPaused && _orleansHandle != null;
    public string? CurrentPosition => _currentPosition;

    public OrleansEventSubscriptionHandleEnhanced(
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

    internal async Task SubscribeAsync(CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(OrleansEventSubscriptionHandleEnhanced));

            if (_orleansHandle != null)
                return;

            Console.WriteLine($"[OrleansEventSubscriptionHandleEnhanced] SubscribeAsync starting for subscription {SubscriptionId}");
            
            _startedAt = DateTime.UtcNow;
            var observer = new EventObserver(HandleEventAsync);
            
            // Orleans Streams require explicit subscription from the beginning
            // Use null token to receive all events from now on
            _orleansHandle = await _stream.SubscribeAsync(observer, token: null);
            
            Console.WriteLine($"[OrleansEventSubscriptionHandleEnhanced] SubscribeAsync completed for subscription {SubscriptionId}, handle: {_orleansHandle}");
        }
        finally
        {
            _semaphore.Release();
        }
    }

    internal async Task SubscribeFromAsync(string fromPosition, CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(OrleansEventSubscriptionHandleEnhanced));

            if (_orleansHandle != null)
                return;

            _startedAt = DateTime.UtcNow;
            var observer = new EventObserver(HandleEventAsync);
            
            StreamSequenceToken? token = null;
            // In production, deserialize token from position
            
            _orleansHandle = await _stream.SubscribeAsync(observer, token);
            _currentPosition = fromPosition;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task HandleEventAsync(Event evt, StreamSequenceToken? token)
    {
        Console.WriteLine($"[OrleansEventSubscriptionHandleEnhanced] Received event {evt.EventType} for subscription {SubscriptionId}");
        
        if (_disposed || _isPaused)
            return;

        var stopwatch = Stopwatch.StartNew();
        
        lock (_statsLock)
        {
            _eventsReceived++;
            _lastEventReceivedAt = DateTime.UtcNow;
        }

        // Apply filter if configured
        if (_filter != null && !_filter.ShouldInclude(evt))
        {
            Console.WriteLine($"[OrleansEventSubscriptionHandleEnhanced] Event filtered out for subscription {SubscriptionId}");
            return;
        }

        try
        {
            Console.WriteLine($"[OrleansEventSubscriptionHandleEnhanced] Processing event {evt.EventType} for subscription {SubscriptionId}");
            
            // Process the event
            await _onEventReceived(evt);
            
            stopwatch.Stop();
            
            lock (_statsLock)
            {
                _eventsProcessed++;
                _lastEventProcessedAt = DateTime.UtcNow;
                _currentPosition = evt.SortableUniqueIdValue;
                
                // Track processing time (keep last 100 for average)
                _processingTimes.Add(stopwatch.Elapsed.TotalMilliseconds);
                if (_processingTimes.Count > 100)
                    _processingTimes.RemoveAt(0);
            }
            
            Console.WriteLine($"[OrleansEventSubscriptionHandleEnhanced] Event processed successfully for subscription {SubscriptionId}");
        }
        catch (Exception ex)
        {
            lock (_statsLock)
            {
                _errorCount++;
                _lastError = ex.Message;
                _lastErrorAt = DateTime.UtcNow;
            }
            
            // Log the error but continue processing
            Console.WriteLine($"Error processing event in subscription {SubscriptionId}: {ex.Message}");
        }
    }

    public async Task PauseAsync()
    {
        await _semaphore.WaitAsync();
        try
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(OrleansEventSubscriptionHandleEnhanced));

            _isPaused = true;
            _pausedAt = DateTime.UtcNow;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public async Task ResumeAsync()
    {
        await _semaphore.WaitAsync();
        try
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(OrleansEventSubscriptionHandleEnhanced));

            _isPaused = false;
            _pausedAt = null;
        }
        finally
        {
            _semaphore.Release();
        }
    }

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

    public IEventSubscriptionStatus GetStatus()
    {
        lock (_statsLock)
        {
            double? avgProcessingTime = null;
            if (_processingTimes.Any())
            {
                avgProcessingTime = _processingTimes.Average();
            }

            return new OrleansEventSubscriptionStatus(
                SubscriptionId,
                IsActive,
                _isPaused,
                _currentPosition,
                _startedAt,
                _pausedAt,
                _eventsReceived,
                _eventsProcessed,
                _errorCount,
                _lastError,
                _lastErrorAt,
                _lastEventReceivedAt,
                _lastEventProcessedAt,
                avgProcessingTime);
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        if (disposing)
        {
            UnsubscribeAsync().GetAwaiter().GetResult();
            _semaphore?.Dispose();
        }
    }

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

        public Task OnCompletedAsync() => Task.CompletedTask;

        public Task OnErrorAsync(Exception ex)
        {
            Console.WriteLine($"Stream error: {ex.Message}");
            return Task.CompletedTask;
        }
    }
}