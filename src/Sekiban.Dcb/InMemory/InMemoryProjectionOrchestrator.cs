using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Domains;
using Sekiban.Dcb.Events;
using System.Collections.Concurrent;
namespace Sekiban.Dcb.InMemory;

/// <summary>
///     In-memory implementation of projection orchestrator for testing
///     Simulates streaming and persistence without Orleans dependencies
/// </summary>
public class InMemoryProjectionOrchestrator : DefaultProjectionOrchestrator
{
    private readonly InMemoryPersistenceStore _persistenceStore;
    private readonly ConcurrentBag<Event> _streamBuffer = new();
    private readonly List<Event> _processedStreamEvents = new();
    private bool _streamingEnabled;

    public InMemoryProjectionOrchestrator(
        DcbDomainTypes domainTypes,
        string projectorName,
        Storage.IEventStore? eventStore = null,
        InMemoryPersistenceStore? persistenceStore = null,
        TimeSpan? safeWindowDuration = null)
        : base(domainTypes, projectorName, eventStore ?? new InMemoryEventStore(), safeWindowDuration)
    {
        _persistenceStore = persistenceStore ?? new InMemoryPersistenceStore();
    }

    /// <summary>
    ///     Simulate streaming event arrival
    /// </summary>
    public async Task<ResultBox<ProcessResult>> SimulateStreamEventAsync(Event evt)
    {
        _streamBuffer.Add(evt);
        var result = await ProcessStreamEventAsync(evt, new StreamContext(true, "test-stream"));
        
        if (result.IsSuccess)
        {
            _processedStreamEvents.Add(evt);
        }
        
        return result;
    }

    /// <summary>
    ///     Simulate batch streaming
    /// </summary>
    public async Task<ResultBox<int>> SimulateStreamBatchAsync(IEnumerable<Event> events)
    {
        var processed = 0;
        foreach (var evt in events)
        {
            var result = await SimulateStreamEventAsync(evt);
            if (result.IsSuccess && result.GetValue().ProcessedCount > 0)
            {
                processed++;
            }
        }
        return ResultBox.FromValue(processed);
    }

    /// <summary>
    ///     Persist current state to in-memory store
    /// </summary>
    public async Task<ResultBox<bool>> PersistAsync()
    {
        try
        {
            var stateResult = await SerializeStateAsync();
            if (!stateResult.IsSuccess)
            {
                return ResultBox.Error<bool>(stateResult.GetException());
            }

            var state = stateResult.GetValue();
            var saved = await _persistenceStore.SaveAsync(state.ProjectorName, state);
            return ResultBox.FromValue(saved);
        }
        catch (Exception ex)
        {
            return ResultBox.Error<bool>(ex);
        }
    }

    /// <summary>
    ///     Load persisted state from in-memory store
    /// </summary>
    public async Task<ResultBox<bool>> LoadPersistedStateAsync()
    {
        try
        {
            var currentStateResult = await GetCurrentStateAsync();
            if (!currentStateResult.IsSuccess)
            {
                return ResultBox.Error<bool>(currentStateResult.GetException());
            }

            var projectorName = currentStateResult.GetValue().ProjectorName;
            var persistedState = await _persistenceStore.LoadAsync(projectorName);
            
            if (persistedState == null)
            {
                return ResultBox.FromValue(false);
            }

            var restoreResult = await RestoreStateAsync(persistedState);
            return restoreResult;
        }
        catch (Exception ex)
        {
            return ResultBox.Error<bool>(ex);
        }
    }

    /// <summary>
    ///     Simulate catch-up from event store
    /// </summary>
    public async Task<ResultBox<CatchUpResult>> SimulateCatchUpAsync(
        int batchSize = 100,
        string? fromPosition = null)
    {
        try
        {
            var totalProcessed = 0;
            var batches = 0;
            var lastPosition = fromPosition;
            var safePosition = fromPosition;

            while (true)
            {
                var eventsResult = await LoadEventsFromStoreAsync(lastPosition, batchSize);
                if (!eventsResult.IsSuccess)
                {
                    return ResultBox.Error<CatchUpResult>(eventsResult.GetException());
                }

                var events = eventsResult.GetValue();
                if (!events.Any())
                {
                    break;
                }

                var context = new ProcessingContext(
                    IsStreaming: false,
                    CheckDuplicates: true,
                    BatchSize: batchSize,
                    SafeWindow: TimeSpan.FromSeconds(20));

                var processResult = await ProcessEventsAsync(events, context);
                if (!processResult.IsSuccess)
                {
                    return ResultBox.Error<CatchUpResult>(processResult.GetException());
                }

                var result = processResult.GetValue();
                totalProcessed += result.ProcessedCount;
                lastPosition = result.LastPosition;
                safePosition = result.SafePosition ?? safePosition;
                batches++;

                if (events.Count < batchSize)
                {
                    break;
                }
            }

            return ResultBox.FromValue(new CatchUpResult(
                totalProcessed,
                batches,
                lastPosition,
                safePosition));
        }
        catch (Exception ex)
        {
            return ResultBox.Error<CatchUpResult>(ex);
        }
    }

    /// <summary>
    ///     Enable/disable streaming simulation
    /// </summary>
    public void SetStreamingEnabled(bool enabled)
    {
        _streamingEnabled = enabled;
    }

    /// <summary>
    ///     Get stream buffer for testing
    /// </summary>
    public IReadOnlyList<Event> GetStreamBuffer()
    {
        return _streamBuffer.ToList();
    }

    /// <summary>
    ///     Get processed stream events for testing
    /// </summary>
    public IReadOnlyList<Event> GetProcessedStreamEvents()
    {
        return _processedStreamEvents;
    }

    /// <summary>
    ///     Clear all test data
    /// </summary>
    public async Task ClearAsync()
    {
        _streamBuffer.Clear();
        _processedStreamEvents.Clear();
        await _persistenceStore.ClearAsync();
    }

    /// <summary>
    ///     Get persistence statistics for testing
    /// </summary>
    public async Task<PersistenceStatistics> GetPersistenceStatisticsAsync()
    {
        return await _persistenceStore.GetStatisticsAsync();
    }
}

/// <summary>
///     Result of catch-up operation
/// </summary>
public record CatchUpResult(
    int TotalProcessed,
    int Batches,
    string? LastPosition,
    string? SafePosition);