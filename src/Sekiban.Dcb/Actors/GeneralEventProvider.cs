using System.Collections.Concurrent;
using ResultBoxes;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.Actors;

/// <summary>
///     General event provider that manages both historical and live event streaming
/// </summary>
public class GeneralEventProvider : IGeneralEventProvider
{
    private readonly IEventStore _eventStore;
    private readonly IEventSubscription _eventSubscription;
    private readonly TimeSpan _safeWindowDuration;
    private readonly object _stateLock = new();
    
    private EventProviderState _state = EventProviderState.NotStarted;
    private SortableUniqueId? _currentPosition;
    private bool _isCaughtUp;
    private readonly ConcurrentDictionary<string, EventProviderHandle> _handles = new();
    private bool _disposed;

    public GeneralEventProvider(
        IEventStore eventStore, 
        IEventSubscription eventSubscription,
        TimeSpan? safeWindowDuration = null)
    {
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _eventSubscription = eventSubscription ?? throw new ArgumentNullException(nameof(eventSubscription));
        _safeWindowDuration = safeWindowDuration ?? TimeSpan.FromSeconds(20);
    }

    public EventProviderState State => _state;
    public SortableUniqueId? CurrentPosition => _currentPosition;
    public bool IsCaughtUp => _isCaughtUp;

    public SortableUniqueId GetSafeWindowThreshold()
    {
        var threshold = DateTime.UtcNow.Subtract(_safeWindowDuration);
        return new SortableUniqueId(SortableUniqueId.Generate(threshold, Guid.Empty));
    }

    // Interface implementation (backward compatible)
    public async Task<IEventProviderHandle> StartAsync(
        Func<Event, bool, Task> onEvent,
        SortableUniqueId? fromPosition = null,
        string eventTopic = "event.all",
        IEventProviderFilter? filter = null,
        int batchSize = 10000,
        CancellationToken cancellationToken = default)
    {
        return await StartAsyncWithRetry(onEvent, fromPosition, eventTopic, filter, batchSize, 
            false, null, cancellationToken);
    }
    
    // Overload with retry options
    public async Task<IEventProviderHandle> StartAsyncWithRetry(
        Func<Event, bool, Task> onEvent,
        SortableUniqueId? fromPosition = null,
        string eventTopic = "event.all",
        IEventProviderFilter? filter = null,
        int batchSize = 10000,
        bool autoRetryOnIncompleteWindow = false,
        TimeSpan? retryDelay = null,
        CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(GeneralEventProvider));

        var handle = new EventProviderHandle(
            Guid.NewGuid().ToString(),
            () => RemoveHandle(Guid.NewGuid().ToString()));

        _handles[handle.ProviderId] = handle;

        // Start the event streaming process
        _ = Task.Run(async () =>
        {
            try
            {
                await StreamEventsAsync(
                    handle,
                    onEvent,
                    fromPosition,
                    eventTopic,
                    filter,
                    batchSize,
                    autoRetryOnIncompleteWindow,
                    retryDelay ?? TimeSpan.FromSeconds(5),
                    cancellationToken);
            }
            catch (Exception ex)
            {
                handle.SetError(ex);
                UpdateState(EventProviderState.Error);
            }
        }, cancellationToken);

        return handle;
    }

    // Interface implementation (backward compatible)
    public async Task<IEventProviderHandle> StartWithActorAsync(
        IMultiProjectionActorCommon actor,
        SortableUniqueId? fromPosition = null,
        string eventTopic = "event.all",
        IEventProviderFilter? filter = null,
        int batchSize = 10000,
        CancellationToken cancellationToken = default)
    {
        return await StartWithActorAsyncWithRetry(actor, fromPosition, eventTopic, filter,
            batchSize, false, null, cancellationToken);
    }
    
    // Overload with retry options
    public async Task<IEventProviderHandle> StartWithActorAsyncWithRetry(
        IMultiProjectionActorCommon actor,
        SortableUniqueId? fromPosition = null,
        string eventTopic = "event.all",
        IEventProviderFilter? filter = null,
        int batchSize = 10000,
        bool autoRetryOnIncompleteWindow = false,
        TimeSpan? retryDelay = null,
        CancellationToken cancellationToken = default)
    {
        // Create batch callback that sends to actor
        return await StartWithBatchCallbackAsyncWithRetry(
            async batch =>
            {
                var events = batch.Select(b => b.evt).ToList();
                var allSafe = batch.All(b => b.isSafe);
                
                // Send to actor's AddEventsAsync
                // finishedCatchUp is true when all events are safe
                await actor.AddEventsAsync(events, allSafe);
            },
            fromPosition,
            eventTopic,
            filter,
            batchSize,
            autoRetryOnIncompleteWindow,
            retryDelay,
            cancellationToken);
    }

    // Interface implementation (backward compatible)
    public async Task<IEventProviderHandle> StartWithBatchCallbackAsync(
        Func<IReadOnlyList<(Event evt, bool isSafe)>, Task> onEventBatch,
        SortableUniqueId? fromPosition = null,
        string eventTopic = "event.all",
        IEventProviderFilter? filter = null,
        int batchSize = 10000,
        CancellationToken cancellationToken = default)
    {
        return await StartWithBatchCallbackAsyncWithRetry(onEventBatch, fromPosition, eventTopic,
            filter, batchSize, false, null, cancellationToken);
    }
    
    // Overload with retry options
    public async Task<IEventProviderHandle> StartWithBatchCallbackAsyncWithRetry(
        Func<IReadOnlyList<(Event evt, bool isSafe)>, Task> onEventBatch,
        SortableUniqueId? fromPosition = null,
        string eventTopic = "event.all",
        IEventProviderFilter? filter = null,
        int batchSize = 10000,
        bool autoRetryOnIncompleteWindow = false,
        TimeSpan? retryDelay = null,
        CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(GeneralEventProvider));

        var handle = new EventProviderHandle(
            Guid.NewGuid().ToString(),
            () => RemoveHandle(Guid.NewGuid().ToString()));

        _handles[handle.ProviderId] = handle;

        // Start the event streaming process with batch callback
        _ = Task.Run(async () =>
        {
            try
            {
                await StreamEventsBatchAsync(
                    handle,
                    onEventBatch,
                    fromPosition,
                    eventTopic,
                    filter,
                    batchSize,
                    autoRetryOnIncompleteWindow,
                    retryDelay ?? TimeSpan.FromSeconds(5),
                    cancellationToken);
            }
            catch (Exception ex)
            {
                handle.SetError(ex);
                UpdateState(EventProviderState.Error);
            }
        }, cancellationToken);

        return handle;
    }

    private async Task StreamEventsAsync(
        EventProviderHandle handle,
        Func<Event, bool, Task> onEvent,
        SortableUniqueId? fromPosition,
        string eventTopic,
        IEventProviderFilter? filter,
        int batchSize,
        bool autoRetryOnIncompleteWindow,
        TimeSpan retryDelay,
        CancellationToken cancellationToken)
    {
        UpdateState(EventProviderState.CatchingUp);
        handle.SetState(EventProviderState.CatchingUp);
        var catchUpStartTime = DateTime.UtcNow;

        // Phase 1: Catch up on historical events from EventStore
        var historicalPosition = fromPosition;
        var processedEventIds = new HashSet<Guid>();
        var safeThreshold = GetSafeWindowThreshold();
        
        // Buffer for events from subscription that arrive during catch-up
        var subscriptionBuffer = new ConcurrentBag<Event>();
        IEventSubscriptionHandle? subscriptionHandle = null;

        try
        {
            // Start subscription immediately but buffer events
            subscriptionHandle = await _eventSubscription.SubscribeAsync(
                async evt =>
                {
                    if (!_isCaughtUp)
                    {
                        // Buffer events during catch-up
                        subscriptionBuffer.Add(evt);
                    }
                    else if (!handle.IsPaused && !handle.IsStopped)
                    {
                        // Process live events directly after catch-up
                        await ProcessLiveEvent(evt, onEvent, filter, handle, processedEventIds);
                    }
                },
                subscriptionId: $"provider-{handle.ProviderId}",
                cancellationToken: cancellationToken);

            // Process historical events in batches
            var processedInCurrentBatch = 0;
            while (!cancellationToken.IsCancellationRequested && !handle.IsStopped)
            {
                if (handle.IsPaused)
                {
                    await Task.Delay(100, cancellationToken);
                    continue;
                }

                ((EventProviderHandle)handle).SetProcessingBatch(true);

                // Get batch of events from EventStore
                var result = await GetFilteredEvents(historicalPosition, filter);
                if (!result.IsSuccess)
                {
                    throw new Exception($"Failed to read events: {result.GetException()}");
                }

                var events = result.GetValue().Take(batchSize - processedInCurrentBatch).ToList();
                if (!events.Any())
                {
                    // No more historical events
                    ((EventProviderHandle)handle).SetProcessingBatch(false);
                    break;
                }

                // Process each event in the batch
                foreach (var evt in events)
                {
                    if (cancellationToken.IsCancellationRequested || handle.IsStopped)
                        break;

                    // Skip if already processed
                    if (!processedEventIds.Add(evt.Id))
                        continue;

                    // Determine if event is safe
                    bool isSafe = string.Compare(evt.SortableUniqueIdValue, safeThreshold.Value, StringComparison.Ordinal) <= 0;

                    // Check filter
                    if (filter != null)
                    {
                        var tags = await GetEventTags(evt);
                        if (!filter.ShouldInclude(evt, tags))
                            continue;
                    }

                    // Send event to projection
                    await onEvent(evt, isSafe);
                    
                    handle.IncrementStatistics(isSafe);
                    UpdatePosition(evt.SortableUniqueIdValue);
                    handle.UpdatePosition(evt.SortableUniqueIdValue);
                    
                    historicalPosition = new SortableUniqueId(evt.SortableUniqueIdValue);
                    processedInCurrentBatch++;
                }

                ((EventProviderHandle)handle).SetProcessingBatch(false);

                // Check if we've reached batch size limit
                if (processedInCurrentBatch >= batchSize)
                {
                    // Batch complete, wait before processing next batch if not caught up
                    processedInCurrentBatch = 0;
                    await Task.Delay(100, cancellationToken); // Small delay between batches
                }

                // Check if we've caught up (no unsafe events in this batch)
                var lastEvent = events.Last();
                if (string.Compare(lastEvent.SortableUniqueIdValue, safeThreshold.Value, StringComparison.Ordinal) > 0)
                {
                    // We're in the unsafe zone, time to transition
                    break;
                }
                
                // If we processed a full batch but didn't reach SafeWindow, handle retry logic
                if (events.Count == batchSize && processedInCurrentBatch >= batchSize)
                {
                    processedInCurrentBatch = 0;
                    
                    if (autoRetryOnIncompleteWindow)
                    {
                        // Wait before retrying
                        await Task.Delay(retryDelay, cancellationToken);
                        
                        // Update safe threshold for next iteration
                        safeThreshold = GetSafeWindowThreshold();
                    }
                    else
                    {
                        // Signal that manual retry is needed
                        ((EventProviderHandle)handle).SetWaitingForManualRetry(true);
                        
                        // Wait for manual retry signal
                        while (((EventProviderHandle)handle).IsWaitingForManualRetry && !cancellationToken.IsCancellationRequested && !handle.IsStopped)
                        {
                            await Task.Delay(100, cancellationToken);
                        }
                        
                        // Update safe threshold after manual retry
                        safeThreshold = GetSafeWindowThreshold();
                    }
                }
            }

            // Phase 2: Process buffered subscription events
            var bufferedEvents = subscriptionBuffer.OrderBy(e => e.SortableUniqueIdValue).ToList();
            foreach (var evt in bufferedEvents)
            {
                if (cancellationToken.IsCancellationRequested || handle.IsStopped)
                    break;

                // Skip if already processed during historical phase
                if (!processedEventIds.Add(evt.Id))
                    continue;

                // These are all unsafe events
                await ProcessLiveEvent(evt, onEvent, filter, handle, processedEventIds);
            }

            // Phase 3: Mark as caught up and switch to live mode
            _isCaughtUp = true;
            handle.SetCaughtUp(DateTime.UtcNow - catchUpStartTime);
            UpdateState(EventProviderState.Live);
            handle.SetState(EventProviderState.Live);

            // Keep alive while subscription is active
            while (!cancellationToken.IsCancellationRequested && !handle.IsStopped)
            {
                await Task.Delay(1000, cancellationToken);
            }
        }
        finally
        {
            subscriptionHandle?.Dispose();
        }
    }

    private async Task StreamEventsBatchAsync(
        EventProviderHandle handle,
        Func<IReadOnlyList<(Event evt, bool isSafe)>, Task> onEventBatch,
        SortableUniqueId? fromPosition,
        string eventTopic,
        IEventProviderFilter? filter,
        int batchSize,
        bool autoRetryOnIncompleteWindow,
        TimeSpan retryDelay,
        CancellationToken cancellationToken)
    {
        UpdateState(EventProviderState.CatchingUp);
        handle.SetState(EventProviderState.CatchingUp);
        var catchUpStartTime = DateTime.UtcNow;

        // Phase 1: Catch up on historical events from EventStore
        var historicalPosition = fromPosition;
        var processedEventIds = new HashSet<Guid>();
        var safeThreshold = GetSafeWindowThreshold();
        
        // Buffer for events from subscription
        var subscriptionBuffer = new ConcurrentBag<Event>();
        IEventSubscriptionHandle? subscriptionHandle = null;

        try
        {
            // Start subscription immediately but buffer events
            subscriptionHandle = await _eventSubscription.SubscribeAsync(
                async evt =>
                {
                    if (!_isCaughtUp)
                    {
                        subscriptionBuffer.Add(evt);
                    }
                    else if (!handle.IsPaused && !handle.IsStopped && !((EventProviderHandle)handle).IsSubscriptionStopped)
                    {
                        // Process live events in batches
                        subscriptionBuffer.Add(evt);
                    }
                },
                subscriptionId: $"provider-{handle.ProviderId}",
                cancellationToken: cancellationToken);

            // Process historical events in batches
            while (!cancellationToken.IsCancellationRequested && !handle.IsStopped)
            {
                if (handle.IsPaused)
                {
                    await Task.Delay(100, cancellationToken);
                    continue;
                }

                ((EventProviderHandle)handle).SetProcessingBatch(true);
                var batch = new List<(Event evt, bool isSafe)>();

                // Get batch of events from EventStore
                var result = await GetFilteredEvents(historicalPosition, filter);
                if (!result.IsSuccess)
                {
                    throw new Exception($"Failed to read events: {result.GetException()}");
                }

                var events = result.GetValue().Take(batchSize).ToList();
                if (!events.Any())
                {
                    // No more historical events
                    ((EventProviderHandle)handle).SetProcessingBatch(false);
                    break;
                }

                // Collect events for batch
                foreach (var evt in events)
                {
                    if (!processedEventIds.Add(evt.Id))
                        continue;

                    bool isSafe = string.Compare(evt.SortableUniqueIdValue, safeThreshold.Value, StringComparison.Ordinal) <= 0;

                    if (filter != null)
                    {
                        var tags = await GetEventTags(evt);
                        if (!filter.ShouldInclude(evt, tags))
                            continue;
                    }

                    batch.Add((evt, isSafe));
                    historicalPosition = new SortableUniqueId(evt.SortableUniqueIdValue);
                }

                // Send batch to callback
                if (batch.Any())
                {
                    await onEventBatch(batch);
                    
                    foreach (var item in batch)
                    {
                        handle.IncrementStatistics(item.isSafe);
                        UpdatePosition(item.evt.SortableUniqueIdValue);
                        handle.UpdatePosition(item.evt.SortableUniqueIdValue);
                    }
                }

                ((EventProviderHandle)handle).SetProcessingBatch(false);

                // Check if we've caught up
                var lastEvent = events.Last();
                if (string.Compare(lastEvent.SortableUniqueIdValue, safeThreshold.Value, StringComparison.Ordinal) > 0)
                {
                    break;
                }

                // If we processed a full batch but didn't reach SafeWindow, handle retry logic
                if (events.Count == batchSize && autoRetryOnIncompleteWindow)
                {
                    // Wait before retrying
                    await Task.Delay(retryDelay, cancellationToken);
                    
                    // Update safe threshold for next iteration
                    safeThreshold = GetSafeWindowThreshold();
                    continue;
                }
                else if (events.Count == batchSize && !autoRetryOnIncompleteWindow)
                {
                    // Signal that manual retry is needed
                    ((EventProviderHandle)handle).SetWaitingForManualRetry(true);
                    
                    // Wait for manual retry signal
                    while (((EventProviderHandle)handle).IsWaitingForManualRetry && !cancellationToken.IsCancellationRequested && !handle.IsStopped)
                    {
                        await Task.Delay(100, cancellationToken);
                    }
                    
                    // Update safe threshold after manual retry
                    safeThreshold = GetSafeWindowThreshold();
                    continue;
                }

                // Small delay between batches
                await Task.Delay(100, cancellationToken);
            }

            // Mark as caught up
            _isCaughtUp = true;
            handle.SetCaughtUp(DateTime.UtcNow - catchUpStartTime);
            UpdateState(EventProviderState.Live);
            handle.SetState(EventProviderState.Live);

            // Process live events in batches
            while (!cancellationToken.IsCancellationRequested && !handle.IsStopped)
            {
                if (handle.IsPaused || ((EventProviderHandle)handle).IsSubscriptionStopped)
                {
                    await Task.Delay(100, cancellationToken);
                    
                    // If subscription is stopped but there are buffered events, process them
                    if (((EventProviderHandle)handle).IsSubscriptionStopped && subscriptionBuffer.Any())
                    {
                        var buffered = subscriptionBuffer.ToList();
                        subscriptionBuffer.Clear();
                        
                        var batch = new List<(Event evt, bool isSafe)>();
                        foreach (var evt in buffered.OrderBy(e => e.SortableUniqueIdValue))
                        {
                            if (!processedEventIds.Add(evt.Id))
                                continue;

                            if (filter != null)
                            {
                                var tags = await GetEventTags(evt);
                                if (!filter.ShouldInclude(evt, tags))
                                    continue;
                            }

                            batch.Add((evt, false)); // Live events are unsafe
                        }

                        if (batch.Any())
                        {
                            await onEventBatch(batch);
                            foreach (var item in batch)
                            {
                                handle.IncrementStatistics(item.isSafe);
                                UpdatePosition(item.evt.SortableUniqueIdValue);
                                handle.UpdatePosition(item.evt.SortableUniqueIdValue);
                            }
                        }
                    }
                    
                    continue;
                }

                // Collect batch from subscription buffer
                if (subscriptionBuffer.Count >= batchSize || 
                    (subscriptionBuffer.Any() && DateTime.UtcNow.Subtract(((EventProviderHandle)handle).LastBatchTime) > TimeSpan.FromSeconds(1)))
                {
                    ((EventProviderHandle)handle).SetProcessingBatch(true);
                    
                    var buffered = subscriptionBuffer.ToList();
                    subscriptionBuffer.Clear();
                    
                    var batch = new List<(Event evt, bool isSafe)>();
                    foreach (var evt in buffered.OrderBy(e => e.SortableUniqueIdValue).Take(batchSize))
                    {
                        if (!processedEventIds.Add(evt.Id))
                            continue;

                        if (filter != null)
                        {
                            var tags = await GetEventTags(evt);
                            if (!filter.ShouldInclude(evt, tags))
                                continue;
                        }

                        batch.Add((evt, false)); // Live events are always unsafe
                    }

                    if (batch.Any())
                    {
                        await onEventBatch(batch);
                        foreach (var item in batch)
                        {
                            handle.IncrementStatistics(item.isSafe);
                            UpdatePosition(item.evt.SortableUniqueIdValue);
                            handle.UpdatePosition(item.evt.SortableUniqueIdValue);
                        }
                    }
                    
                    ((EventProviderHandle)handle).SetProcessingBatch(false);
                    ((EventProviderHandle)handle).UpdateLastBatchTime();
                }

                await Task.Delay(100, cancellationToken);
            }
        }
        finally
        {
            subscriptionHandle?.Dispose();
        }
    }

    private async Task ProcessLiveEvent(
        Event evt,
        Func<Event, bool, Task> onEvent,
        IEventProviderFilter? filter,
        EventProviderHandle handle,
        HashSet<Guid> processedEventIds)
    {
        // Skip duplicates
        if (!processedEventIds.Add(evt.Id))
            return;

        // Check filter
        if (filter != null)
        {
            var tags = await GetEventTags(evt);
            if (!filter.ShouldInclude(evt, tags))
                return;
        }

        // Live events are always unsafe
        await onEvent(evt, false);
        
        handle.IncrementStatistics(false);
        UpdatePosition(evt.SortableUniqueIdValue);
        handle.UpdatePosition(evt.SortableUniqueIdValue);
    }

    private async Task<ResultBox<IEnumerable<Event>>> GetFilteredEvents(
        SortableUniqueId? fromPosition, 
        IEventProviderFilter? filter)
    {
        // If we have tag filters, use ReadEventsByTagAsync
        var tagFilters = filter?.GetTagFilters()?.ToList();
        if (tagFilters?.Any() == true)
        {
            // For simplicity, use the first tag (in real implementation, might need to handle multiple)
            return await _eventStore.ReadEventsByTagAsync(tagFilters.First(), fromPosition);
        }

        // Otherwise, read all events
        return await _eventStore.ReadAllEventsAsync(fromPosition);
    }

    private async Task<List<ITag>> GetEventTags(Event evt)
    {
        // In a real implementation, this would retrieve tags associated with the event
        // For now, return empty list (tags would typically be stored with events)
        return await Task.FromResult(new List<ITag>());
    }

    private void UpdateState(EventProviderState newState)
    {
        lock (_stateLock)
        {
            _state = newState;
        }
    }

    private void UpdatePosition(string position)
    {
        _currentPosition = new SortableUniqueId(position);
    }

    private void RemoveHandle(string providerId)
    {
        _handles.TryRemove(providerId, out _);
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        foreach (var handle in _handles.Values)
        {
            handle.Dispose();
        }
        
        _handles.Clear();
        _disposed = true;
    }

    private class EventProviderHandle : IEventProviderHandle
    {
        private readonly Action _onDispose;
        private readonly TaskCompletionSource<bool> _catchUpTcs = new();
        private volatile EventProviderState _state = EventProviderState.NotStarted;
        private volatile bool _isPaused;
        private volatile bool _isStopped;
        private volatile bool _isSubscriptionStopped;
        private volatile bool _isProcessingBatch;
        private long _totalEvents;
        private long _safeEvents;
        private long _unsafeEvents;
        private DateTime? _lastEventTime;
        private SortableUniqueId? _lastEventPosition;
        private TimeSpan? _catchUpDuration;
        private Exception? _error;
        private bool _disposed;
        private readonly TaskCompletionSource<bool> _batchCompletionTcs = new();
        private DateTime _lastBatchTime = DateTime.UtcNow;
        private volatile bool _isWaitingForManualRetry;

        public EventProviderHandle(string providerId, Action onDispose)
        {
            ProviderId = providerId;
            _onDispose = onDispose;
        }

        public string ProviderId { get; }
        public EventProviderState State => _state;
        public bool IsPaused => _isPaused;
        public bool IsStopped => _isStopped;
        public bool IsSubscriptionStopped => _isSubscriptionStopped;
        public bool IsProcessingBatch => _isProcessingBatch;
        public DateTime LastBatchTime => _lastBatchTime;
        public bool IsWaitingForManualRetry => _isWaitingForManualRetry;

        public void SetState(EventProviderState state) => _state = state;
        
        public void SetError(Exception ex)
        {
            _error = ex;
            _state = EventProviderState.Error;
            _catchUpTcs.TrySetException(ex);
        }

        public void SetCaughtUp(TimeSpan duration)
        {
            _catchUpDuration = duration;
            _catchUpTcs.TrySetResult(true);
        }

        public void UpdatePosition(string position)
        {
            _lastEventPosition = new SortableUniqueId(position);
            _lastEventTime = DateTime.UtcNow;
        }

        public void IncrementStatistics(bool isSafe)
        {
            Interlocked.Increment(ref _totalEvents);
            if (isSafe)
                Interlocked.Increment(ref _safeEvents);
            else
                Interlocked.Increment(ref _unsafeEvents);
        }

        public async Task PauseAsync()
        {
            _isPaused = true;
            _state = EventProviderState.Paused;
            await Task.CompletedTask;
        }

        public async Task ResumeAsync()
        {
            _isPaused = false;
            _state = _catchUpDuration.HasValue ? EventProviderState.Live : EventProviderState.CatchingUp;
            await Task.CompletedTask;
        }

        public async Task StopAsync()
        {
            _isStopped = true;
            _state = EventProviderState.Stopped;
            await Task.CompletedTask;
            Dispose();
        }

        public async Task StopSubscriptionAsync()
        {
            _isSubscriptionStopped = true;
            await Task.CompletedTask;
        }
        
        public void SetWaitingForManualRetry(bool waiting)
        {
            _isWaitingForManualRetry = waiting;
        }
        
        public void RetryManually()
        {
            _isWaitingForManualRetry = false;
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

        public async Task WaitForCurrentBatchAsync()
        {
            while (_isProcessingBatch)
            {
                await Task.Delay(50);
            }
        }

        public EventProviderStatistics GetStatistics()
        {
            return new EventProviderStatistics(
                Interlocked.Read(ref _totalEvents),
                Interlocked.Read(ref _safeEvents),
                Interlocked.Read(ref _unsafeEvents),
                _lastEventTime,
                _lastEventPosition,
                _catchUpDuration);
        }

        public void SetProcessingBatch(bool isProcessing)
        {
            _isProcessingBatch = isProcessing;
            if (!isProcessing)
            {
                _batchCompletionTcs.TrySetResult(true);
            }
        }

        public void UpdateLastBatchTime()
        {
            _lastBatchTime = DateTime.UtcNow;
        }

        public void Dispose()
        {
            if (_disposed) return;
            
            _isStopped = true;
            _disposed = true;
            _catchUpTcs.TrySetCanceled();
            _batchCompletionTcs.TrySetCanceled();
            _onDispose?.Invoke();
        }
    }
}