using System.Runtime.CompilerServices;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Storage;

namespace Sekiban.Dcb.InMemory;

/// <summary>
///     In-memory implementation of multi-projection event feeder for testing and development
/// </summary>
public class InMemoryMultiProjectionEventFeeder : IMultiProjectionEventFeeder
{
    private readonly IEventStore _eventStore;
    private readonly InMemoryEventSubscription _subscription;
    private readonly Dictionary<string, InMemoryEventFeederHandle> _feeders = new();
    private readonly object _feedersLock = new();

    public InMemoryMultiProjectionEventFeeder(IEventStore eventStore, InMemoryEventSubscription subscription)
    {
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _subscription = subscription ?? throw new ArgumentNullException(nameof(subscription));
    }

    public async Task<IEventFeederHandle> StartFeedingAsync(
        string? fromPosition,
        Func<IReadOnlyList<Event>, Task> onEventBatch,
        int batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        return await StartFeedingWithFilterAsync(
            null,
            fromPosition,
            onEventBatch,
            batchSize,
            cancellationToken);
    }

    public async Task<IEventFeederHandle> StartFeedingWithFilterAsync(
        IEventFilter? filter,
        string? fromPosition,
        Func<IReadOnlyList<Event>, Task> onEventBatch,
        int batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        var feederId = Guid.NewGuid().ToString();
        var feederHandle = new InMemoryEventFeederHandle(
            feederId,
            fromPosition,
            () => RemoveFeeder(feederId));

        lock (_feedersLock)
        {
            _feeders[feederId] = feederHandle;
        }

        // Start the feeding process in background
        _ = Task.Run(async () =>
        {
            try
            {
                await FeedEventsAsync(
                    feederHandle,
                    filter,
                    fromPosition,
                    onEventBatch,
                    batchSize,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                feederHandle.SetError(ex);
            }
        }, cancellationToken);

        return feederHandle;
    }

    public async IAsyncEnumerable<Event> GetEventsInRangeAsync(
        string fromPosition,
        string toPosition,
        int batchSize = 100,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var currentPosition = fromPosition;

        while (!cancellationToken.IsCancellationRequested)
        {
            SortableUniqueId? since = string.IsNullOrEmpty(currentPosition) 
                ? null 
                : new SortableUniqueId(currentPosition);
            
            var result = await _eventStore.ReadAllEventsAsync(since);
            if (!result.IsSuccess)
            {
                yield break;
            }
            
            var events = result.GetValue().Take(batchSize).ToList();

            if (!events.Any())
            {
                break;
            }

            foreach (var evt in events)
            {
                // Check if we've passed the end position
                if (string.Compare(evt.SortableUniqueIdValue, toPosition, StringComparison.Ordinal) > 0)
                {
                    yield break;
                }

                yield return evt;
                currentPosition = evt.SortableUniqueIdValue;
            }

            // If we've reached or passed the end position
            if (string.Compare(currentPosition, toPosition, StringComparison.Ordinal) >= 0)
            {
                break;
            }
        }
    }

    private async Task FeedEventsAsync(
        InMemoryEventFeederHandle handle,
        IEventFilter? filter,
        string? fromPosition,
        Func<IReadOnlyList<Event>, Task> onEventBatch,
        int batchSize,
        CancellationToken cancellationToken)
    {
        // Phase 1: Catch up on historical events
        handle.SetState(EventFeederState.CatchingUp);
        
        var currentPosition = fromPosition;
        var caughtUp = false;

        while (!cancellationToken.IsCancellationRequested && !handle.IsStopped)
        {
            if (handle.IsPaused)
            {
                await Task.Delay(100, cancellationToken);
                continue;
            }

            // Get batch of historical events
            SortableUniqueId? since = string.IsNullOrEmpty(currentPosition) 
                ? null 
                : new SortableUniqueId(currentPosition);
            
            var result = await _eventStore.ReadAllEventsAsync(since);
            if (!result.IsSuccess)
            {
                handle.SetError(new Exception($"Failed to read events: {result.GetException()}"));
                return;
            }
            
            var events = result.GetValue().Take(batchSize).ToList();

            if (!events.Any())
            {
                // No more historical events, we're caught up
                caughtUp = true;
                break;
            }

            // Apply filter if present
            if (filter != null)
            {
                events = events.Where(filter.ShouldInclude).ToList();
            }

            if (events.Any())
            {
                // Process batch
                await onEventBatch(events);
                handle.IncrementEventsProcessed(events.Count);

                // Update position
                currentPosition = events.Last().SortableUniqueIdValue;
                handle.UpdatePosition(currentPosition);
            }
        }

        if (!caughtUp || handle.IsStopped || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        // Phase 2: Subscribe to live events
        handle.SetState(EventFeederState.Live);
        handle.SetCaughtUp();

        var eventBuffer = new List<Event>();
        var lastFlush = DateTime.UtcNow;

        // Subscribe to new events
        var subscriptionHandle = await _subscription.SubscribeFromAsync(
            currentPosition,
            async evt =>
            {
                if (handle.IsStopped || handle.IsPaused) return;

                // Apply filter if present
                if (filter != null && !filter.ShouldInclude(evt))
                {
                    return;
                }

                lock (eventBuffer)
                {
                    eventBuffer.Add(evt);
                }

                // Check if we should flush the buffer
                var shouldFlush = false;
                lock (eventBuffer)
                {
                    shouldFlush = eventBuffer.Count >= batchSize ||
                                  (DateTime.UtcNow - lastFlush) > TimeSpan.FromSeconds(1);
                }

                if (shouldFlush)
                {
                    List<Event> toProcess;
                    lock (eventBuffer)
                    {
                        toProcess = new List<Event>(eventBuffer);
                        eventBuffer.Clear();
                        lastFlush = DateTime.UtcNow;
                    }

                    if (toProcess.Any())
                    {
                        await onEventBatch(toProcess);
                        handle.IncrementEventsProcessed(toProcess.Count);
                        handle.UpdatePosition(toProcess.Last().SortableUniqueIdValue);
                    }
                }
            },
            subscriptionId: $"feeder-{handle.FeederId}",
            cancellationToken);

        handle.SetSubscriptionHandle(subscriptionHandle);

        // Keep alive and flush periodically
        while (!cancellationToken.IsCancellationRequested && !handle.IsStopped)
        {
            await Task.Delay(1000, cancellationToken);

            // Flush any remaining events in buffer
            if (!handle.IsPaused)
            {
                List<Event> toProcess;
                lock (eventBuffer)
                {
                    if (eventBuffer.Count > 0)
                    {
                        toProcess = new List<Event>(eventBuffer);
                        eventBuffer.Clear();
                        lastFlush = DateTime.UtcNow;
                    }
                    else
                    {
                        continue;
                    }
                }

                if (toProcess.Any())
                {
                    await onEventBatch(toProcess);
                    handle.IncrementEventsProcessed(toProcess.Count);
                    handle.UpdatePosition(toProcess.Last().SortableUniqueIdValue);
                }
            }
        }
    }

    private void RemoveFeeder(string feederId)
    {
        lock (_feedersLock)
        {
            _feeders.Remove(feederId);
        }
    }

    private class InMemoryEventFeederHandle : IEventFeederHandle
    {
        private readonly Action _onDispose;
        private readonly TaskCompletionSource<bool> _catchUpTcs = new();
        private volatile EventFeederState _state = EventFeederState.NotStarted;
        private volatile string? _currentPosition;
        private long _eventsProcessed;
        private volatile bool _isCatchingUp = true;
        private volatile bool _isPaused;
        private volatile bool _isStopped;
        private IEventSubscriptionHandle? _subscriptionHandle;
        private Exception? _error;
        private bool _disposed;

        public InMemoryEventFeederHandle(string feederId, string? initialPosition, Action onDispose)
        {
            FeederId = feederId;
            _currentPosition = initialPosition;
            _onDispose = onDispose;
        }

        public string FeederId { get; }
        public EventFeederState State => _state;
        public string? CurrentPosition => _currentPosition;
        public long EventsProcessed => Interlocked.Read(ref _eventsProcessed);
        public bool IsCatchingUp => _isCatchingUp;
        public bool IsPaused => _isPaused;
        public bool IsStopped => _isStopped;

        public void SetState(EventFeederState state) => _state = state;
        public void UpdatePosition(string position) => _currentPosition = position;
        public void IncrementEventsProcessed(int count) => Interlocked.Add(ref _eventsProcessed, count);
        public void SetCaughtUp()
        {
            _isCatchingUp = false;
            _catchUpTcs.TrySetResult(true);
        }
        public void SetError(Exception ex)
        {
            _error = ex;
            _state = EventFeederState.Error;
            _catchUpTcs.TrySetException(ex);
        }
        public void SetSubscriptionHandle(IEventSubscriptionHandle handle) => _subscriptionHandle = handle;

        public Task PauseAsync()
        {
            _isPaused = true;
            _state = EventFeederState.Paused;
            return _subscriptionHandle?.PauseAsync() ?? Task.CompletedTask;
        }

        public Task ResumeAsync()
        {
            _isPaused = false;
            _state = _isCatchingUp ? EventFeederState.CatchingUp : EventFeederState.Live;
            return _subscriptionHandle?.ResumeAsync() ?? Task.CompletedTask;
        }

        public async Task StopAsync()
        {
            _isStopped = true;
            _state = EventFeederState.Stopped;
            
            if (_subscriptionHandle != null)
            {
                await _subscriptionHandle.UnsubscribeAsync();
            }
            
            Dispose();
        }

        public async Task<bool> WaitForCatchUpAsync(TimeSpan timeout)
        {
            try
            {
                var delayTask = Task.Delay(timeout);
                var completedTask = await Task.WhenAny(_catchUpTcs.Task, delayTask);
                return completedTask == _catchUpTcs.Task && _catchUpTcs.Task.Result;
            }
            catch
            {
                return false;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            
            _isStopped = true;
            _disposed = true;
            _subscriptionHandle?.Dispose();
            _onDispose?.Invoke();
            _catchUpTcs.TrySetCanceled();
        }
    }
}