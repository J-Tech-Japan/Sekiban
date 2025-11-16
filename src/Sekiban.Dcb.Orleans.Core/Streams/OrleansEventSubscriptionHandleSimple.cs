using Orleans.Streams;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Events;
namespace Sekiban.Dcb.Orleans.Streams;

/// <summary>
///     Simple Orleans event subscription handle that directly wraps the Orleans stream subscription
/// </summary>
public class OrleansEventSubscriptionHandleSimple : IEventSubscriptionHandle
{
    private readonly Action? _onDispose;
    private readonly StreamSubscriptionHandle<SerializableEvent> _orleansHandle;
    private bool _disposed;

    public OrleansEventSubscriptionHandleSimple(
        string subscriptionId,
        StreamSubscriptionHandle<SerializableEvent> orleansHandle,
        Action? onDispose = null)
    {
        SubscriptionId = subscriptionId ?? throw new ArgumentNullException(nameof(subscriptionId));
        _orleansHandle = orleansHandle ?? throw new ArgumentNullException(nameof(orleansHandle));
        _onDispose = onDispose;
    }

    public string SubscriptionId { get; }
    public bool IsActive => !_disposed && _orleansHandle != null;
    public string? CurrentPosition
    {
        get;
        private set;
    }

    public Task PauseAsync() =>
        // Orleans streams don't support pause/resume directly
        // This would need to be implemented by buffering events
        Task.CompletedTask;

    public Task ResumeAsync() =>
        // Orleans streams don't support pause/resume directly
        Task.CompletedTask;

    public async Task UnsubscribeAsync()
    {
        if (_disposed)
            return;

        await _orleansHandle.UnsubscribeAsync();
        _disposed = true;
        _onDispose?.Invoke();
    }

    public IEventSubscriptionStatus GetStatus() =>
        new OrleansEventSubscriptionStatus(
            SubscriptionId,
            IsActive,
            false, // isPaused
            CurrentPosition,
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

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    public void UpdatePosition(string position)
    {
        CurrentPosition = position;
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
