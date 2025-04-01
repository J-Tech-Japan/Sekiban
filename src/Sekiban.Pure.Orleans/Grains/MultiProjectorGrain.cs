using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ResultBoxes;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
using Sekiban.Pure.Orleans.Parts;
using Sekiban.Pure.Projectors;
using Sekiban.Pure.Query;
using System.Text.Json;
namespace Sekiban.Pure.Orleans.Grains;

public class MultiProjectorGrain : Grain, IMultiProjectorGrain
{
    private static readonly TimeSpan SafeStateTime = TimeSpan.FromSeconds(7);
    
    // In-memory state for the grain
    private MultiProjectionState? multiProjectionState;
    private MultiProjectionState? unsafeState;
    
    // Orleans persistent state using our serializable wrapper
    private readonly IPersistentState<SerializableMultiProjectionState> safeState;
    private readonly IEventReader eventReader;
    private readonly SekibanDomainTypes sekibanDomainTypes;
    private readonly ILogger<MultiProjectorGrain> logger;
    private readonly JsonSerializerOptions jsonSerializerOptions;

    public MultiProjectorGrain(
        [PersistentState("multiProjector", "Default")] IPersistentState<SerializableMultiProjectionState> safeState,
        IEventReader eventReader,
        SekibanDomainTypes sekibanDomainTypes,
        ILogger<MultiProjectorGrain> logger)
    {
        this.safeState = safeState;
        this.eventReader = eventReader;
        this.sekibanDomainTypes = sekibanDomainTypes;
        this.logger = logger;
        
        // Use the same JSON options that are used elsewhere in the application
        this.jsonSerializerOptions = sekibanDomainTypes.JsonSerializerOptions;
    }

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);
        
        // Read persistent state
        await safeState.ReadStateAsync();
        
        // If we have a persistent state, try to convert it to MultiProjectionState
        if (safeState.RecordExists && safeState.State != null)
        {
            // Get the projector type from the grain name
            var projectorType = GetProjectorFromMultiProjectorName();
            
            // Try to convert the serializable state to MultiProjectionState
            var stateResult = await TryRestoreStateAsync(projectorType);
            
            if (stateResult.HasValue)
            {
                // Successfully restored state
                multiProjectionState = stateResult.Value;
                logger.LogInformation("Successfully restored MultiProjectorGrain state from persistent storage");
            }
            else
            {
                // Failed to restore state, we'll rebuild from events
                logger.LogWarning("Failed to restore MultiProjectorGrain state from persistent storage. Rebuilding from events.");
                await RebuildStateAsync();
            }
        }
        else
        {
            // No persistent state exists, initialize from events
            await RebuildStateAsync();
        }
    }

    /// <summary>
    /// Attempts to restore MultiProjectionState from the serializable persistent state
    /// </summary>
    private async Task<OptionalValue<MultiProjectionState>> TryRestoreStateAsync(IMultiProjectorCommon projector)
    {
        try
        {
            // Use the type of the projector to restore the state
            var projectorType = projector.GetType();
            
            // Get the generic method that can handle this specific projector type
            var method = typeof(SerializableMultiProjectionState)
                .GetMethod(nameof(SerializableMultiProjectionState.ToMultiProjectionStateAsync))
                ?.MakeGenericMethod(projectorType);
            
            if (method == null)
            {
                logger.LogError("Could not find ToMultiProjectionStateAsync method");
                return OptionalValue<MultiProjectionState>.None;
            }
            
            // Invoke the generic method on our serializable state
            var task = (Task<OptionalValue<MultiProjectionState>>)method.Invoke(
                safeState.State, 
                new object[] { jsonSerializerOptions })!;
            
            // Wait for the result
            return await task;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error restoring MultiProjectionState");
            return OptionalValue<MultiProjectionState>.None;
        }
    }

    /// <summary>
    /// Saves the current multiProjectionState to persistent storage
    /// </summary>
    private async Task PersistStateAsync(MultiProjectionState state) 
    {
        try
        {
            // Convert MultiProjectionState to SerializableMultiProjectionState
            safeState.State = await SerializableMultiProjectionState.CreateFromAsync(
                state, 
                jsonSerializerOptions);
            
            // Write to persistent storage
            await safeState.WriteStateAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error persisting MultiProjectionState");
            // Continue execution even if persistence fails
        }
    }

    public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        await base.OnDeactivateAsync(reason, cancellationToken);
    }

    public async Task RebuildStateAsync()
    {
        // Get the projector from the grain name
        var projector = GetProjectorFromMultiProjectorName();
        
        // Get all events
        var events = (await eventReader.GetEvents(EventRetrievalInfo.All)).UnwrapBox().ToList();
        
        if (!events.Any())
        {
            // No events, create initial state
            multiProjectionState = new MultiProjectionState(
                projector, 
                Guid.Empty, 
                string.Empty, 
                0, 
                0, 
                "default");
            
            // Save initial state
            await PersistStateAsync(multiProjectionState);
            return;
        }
        
        // Process events to build state
        await UpdateProjectionAsync(events, projector);
    }

    public async Task BuildStateAsync()
    {
        if (multiProjectionState == null)
        {
            // No in-memory state, rebuild from all events
            await RebuildStateAsync();
            return;
        }

        // Use the last sortable ID as checkpoint for incremental update
        var checkpoint = multiProjectionState.LastSortableUniqueId;
        var retrievalInfo = EventRetrievalInfo.All with
        {
            SortableIdCondition = ISortableIdCondition.Since(new SortableUniqueIdValue(checkpoint))
        };

        // Get events since the checkpoint
        var events = (await eventReader.GetEvents(retrievalInfo)).UnwrapBox().ToList();
        
        if (!events.Any())
        {
            // No new events
            return;
        }

        // Apply new events to current state
        await UpdateProjectionAsync(events, multiProjectionState.ProjectorCommon);
    }

    /// <summary>
    /// Updates projection state with new events, handling safe and unsafe states
    /// </summary>
    private async Task UpdateProjectionAsync(List<IEvent> events, IMultiProjectorCommon projector)
    {
        var currentTime = DateTime.UtcNow;
        var safeTimeThreshold = currentTime.Subtract(SafeStateTime);
        // Safety threshold for event timestamps
        var safeThresholdSortable = new SortableUniqueIdValue(safeTimeThreshold.ToString("O"));
        var eventsList = events.ToList();
        var lastEvent = eventsList.Last();
        var lastEventSortable = new SortableUniqueIdValue(lastEvent.SortableUniqueId);

        if (lastEventSortable.IsEarlierThan(safeThresholdSortable))
        {
            // All events are considered "safe" (older than threshold)
            var projectedState = sekibanDomainTypes.MultiProjectorsType.Project(projector, eventsList).UnwrapBox();
            
            // Update in-memory state
            multiProjectionState = new MultiProjectionState(
                projectedState,
                lastEvent.Id,
                lastEvent.SortableUniqueId,
                (multiProjectionState?.Version ?? 0) + 1,
                0,
                multiProjectionState?.RootPartitionKey ?? "default");
            
            // Clear unsafe state
            unsafeState = null;
            
            // Persist to storage
            await PersistStateAsync(multiProjectionState);
        }
        else
        {
            // Some events are "unsafe" (newer than threshold)
            // Find the split point between safe and unsafe events
            var splitIndex = eventsList.FindLastIndex(
                e => new SortableUniqueIdValue(e.SortableUniqueId).IsEarlierThan(safeThresholdSortable));

            if (splitIndex >= 0)
            {
                // Process safe events and persist them
                var safeEvents = eventsList.Take(splitIndex + 1).ToList();
                var lastSafeEvent = safeEvents.Last();
                var safeProjectedState = sekibanDomainTypes.MultiProjectorsType.Project(projector, safeEvents).UnwrapBox();
                
                // Update in-memory state with safe events
                multiProjectionState = new MultiProjectionState(
                    safeProjectedState,
                    lastSafeEvent.Id,
                    lastSafeEvent.SortableUniqueId,
                    (multiProjectionState?.Version ?? 0) + 1,
                    0,
                    multiProjectionState?.RootPartitionKey ?? "default");
                
                // Persist safe state
                await PersistStateAsync(multiProjectionState);
            }

            // Process unsafe events (newer) for in-memory state only
            var unsafeEvents = eventsList.Skip(Math.Max(splitIndex + 1, 0)).ToList();
            var unsafeProjectedState = sekibanDomainTypes
                .MultiProjectorsType
                .Project(multiProjectionState!.ProjectorCommon, unsafeEvents)
                .UnwrapBox();
            
            // Set unsafe state (in-memory only, not persisted)
            unsafeState = new MultiProjectionState(
                unsafeProjectedState,
                lastEvent.Id,
                lastEvent.SortableUniqueId,
                multiProjectionState.Version + 1,
                0,
                multiProjectionState.RootPartitionKey);
        }
    }

    public async Task<MultiProjectionState> GetStateAsync()
    {
        await BuildStateAsync();
        // Return unsafe state if available, otherwise return safe state
        return unsafeState ?? multiProjectionState ?? 
               throw new InvalidOperationException("MultiProjectorGrain state not initialized");
    }

    public async Task<QueryResultGeneral> QueryAsync(IQueryCommon query)
    {
        var result = await sekibanDomainTypes.QueryTypes.ExecuteAsQueryResult(
                query,
                GetProjectorForQuery,
                new ServiceCollection().BuildServiceProvider()) ??
            throw new ApplicationException("Query not found");
        
        return result.Remap(value => value.ToGeneral(query)).UnwrapBox();
    }

    public async Task<IListQueryResult> QueryAsync(IListQueryCommon query)
    {
        var result = await sekibanDomainTypes.QueryTypes.ExecuteAsQueryResult(
                query,
                GetProjectorForQuery,
                new ServiceCollection().BuildServiceProvider()) ??
            throw new ApplicationException("Query not found");
        
        return result.UnwrapBox();
    }

    public async Task<ResultBox<IMultiProjectorStateCommon>> GetProjectorForQuery(
        IMultiProjectionEventSelector multiProjectionEventSelector)
    {
        await BuildStateAsync();
        
        // Use unsafe state if available, otherwise use safe state
        return unsafeState?.ToResultBox<IMultiProjectorStateCommon>() ??
               multiProjectionState?.ToResultBox<IMultiProjectorStateCommon>() ?? 
               new ApplicationException("No state found");
    }

    public IMultiProjectorCommon GetProjectorFromMultiProjectorName()
    {
        var grainName = this.GetPrimaryKeyString();
        return sekibanDomainTypes.MultiProjectorsType.GetProjectorFromMultiProjectorName(grainName);
    }
}
