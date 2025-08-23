using Orleans.Streams;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Events;

namespace Sekiban.Dcb.Orleans.Streams;

/// <summary>
/// Simple Orleans event subscription handle that directly wraps the Orleans stream subscription
/// </summary>
public class OrleansEventSubscriptionHandleSimple : IEventSubscriptionHandle
{
    private readonly StreamSubscriptionHandle<Event> _orleansHandle;
    private readonly Action? _onDispose;
    private string? _currentPosition;
    private bool _disposed;

    public string SubscriptionId { get; }
    public bool IsActive => !_disposed && _orleansHandle != null;
    public string? CurrentPosition => _currentPosition;

    public OrleansEventSubscriptionHandleSimple(
        string subscriptionId,
        StreamSubscriptionHandle<Event> orleansHandle,
        Action? onDispose = null)
    {
        SubscriptionId = subscriptionId ?? throw new ArgumentNullException(nameof(subscriptionId));
        _orleansHandle = orleansHandle ?? throw new ArgumentNullException(nameof(orleansHandle));
        _onDispose = onDispose;
    }

    public Task PauseAsync()
    {
        // Orleans streams don't support pause/resume directly
        // This would need to be implemented by buffering events
        return Task.CompletedTask;
    }

    public Task ResumeAsync()
    {
        // Orleans streams don't support pause/resume directly
        return Task.CompletedTask;
    }

    public async Task UnsubscribeAsync()
    {
        if (_disposed)
            return;

        await _orleansHandle.UnsubscribeAsync();
        _disposed = true;
        _onDispose?.Invoke();
    }

    public IEventSubscriptionStatus GetStatus()
    {
        return new OrleansEventSubscriptionStatus(
            SubscriptionId,
            IsActive,
            false, // isPaused
            _currentPosition,
            null, // startedAt
            null, // pausedAt
            0, // eventsReceived
            0, // eventsProcessed
            0, // errorCount
            null, // lastError
            null, // lastErrorAt
            null, // lastEventReceivedAt
            null, // lastEventProcessedAt
            null); // avgProcessingTime
    }

    public void UpdatePosition(string position)
    {
        _currentPosition = position;
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
        }
    }
}

/// <summary>
/// Observer for Orleans event streams
/// </summary>
public class EventStreamObserver : IAsyncObserver<Event>
{
    private readonly string _subscriptionId;
    private readonly Func<Event, Task> _onEvent;
    private readonly IEventFilter? _filter;

    public EventStreamObserver(
        string subscriptionId,
        Func<Event, Task> onEvent,
        IEventFilter? filter = null)
    {
        _subscriptionId = subscriptionId;
        _onEvent = onEvent;
        _filter = filter;
    }

    public async Task OnNextAsync(Event item, StreamSequenceToken? token = null)
    {
        Console.WriteLine($"[EventStreamObserver] Received event {item.EventType} for subscription {_subscriptionId}");
        
        // Apply filter if configured
        if (_filter != null && !_filter.ShouldInclude(item))
        {
            Console.WriteLine($"[EventStreamObserver] Event filtered out for subscription {_subscriptionId}");
            return;
        }

        try
        {
            await _onEvent(item);
            Console.WriteLine($"[EventStreamObserver] Event processed successfully for subscription {_subscriptionId}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[EventStreamObserver] Error processing event: {ex.Message}");
        }
    }

    public Task OnCompletedAsync()
    {
        Console.WriteLine($"[EventStreamObserver] Stream completed for subscription {_subscriptionId}");
        return Task.CompletedTask;
    }

    public Task OnErrorAsync(Exception ex)
    {
        Console.WriteLine($"[EventStreamObserver] Stream error for subscription {_subscriptionId}: {ex.Message}");
        return Task.CompletedTask;
    }
}