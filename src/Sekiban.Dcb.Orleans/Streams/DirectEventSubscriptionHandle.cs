using Orleans.Streams;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Events;
namespace Sekiban.Dcb.Orleans.Streams;

/// <summary>
///     Handle that wraps an existing Orleans subscription and forwards events
/// </summary>
public class DirectEventSubscriptionHandle : IEventSubscriptionHandle
{
    private readonly Func<Event, Task> _onEventReceived;
    private readonly StreamSubscriptionHandle<Event> _orleansHandle;
    private bool _disposed;
    private long _eventsReceived;

    public DirectEventSubscriptionHandle(
        string subscriptionId,
        StreamSubscriptionHandle<Event> orleansHandle,
        Func<Event, Task> onEventReceived)
    {
        SubscriptionId = subscriptionId;
        _orleansHandle = orleansHandle;
        _onEventReceived = onEventReceived;
    }

    public string SubscriptionId { get; }
    public bool IsActive => !_disposed;
    public string? CurrentPosition
    {
        get;
        private set;
    }

    public Task PauseAsync() =>
        // Not supported for direct subscriptions
        Task.CompletedTask;

    public Task ResumeAsync() =>
        // Not supported for direct subscriptions
        Task.CompletedTask;

    public Task UnsubscribeAsync()
    {
        // We don't actually unsubscribe the Orleans handle
        // That's managed by the grain
        _disposed = true;
        return Task.CompletedTask;
    }

    public IEventSubscriptionStatus GetStatus() =>
        new OrleansEventSubscriptionStatus(
            SubscriptionId,
            IsActive,
            false, // isPaused
            CurrentPosition,
            null, // startedAt
            null, // pausedAt
            _eventsReceived, // eventsReceived
            _eventsReceived, // eventsProcessed
            0, // errorCount
            null, // lastError
            null, // lastErrorAt
            null, // lastEventReceivedAt
            null, // lastEventProcessedAt
            null); // avgProcessingTime

    public void Dispose()
    {
        _disposed = true;
    }

    public async Task ProcessEventAsync(Event evt)
    {
        if (_disposed)
            return;

        try
        {
            Console.WriteLine(
                $"[DirectEventSubscriptionHandle] Processing event {evt.EventType} for subscription {SubscriptionId}");
            await _onEventReceived(evt);
            CurrentPosition = evt.SortableUniqueIdValue;
            _eventsReceived++;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DirectEventSubscriptionHandle] Error processing event: {ex.Message}");
        }
    }
}
