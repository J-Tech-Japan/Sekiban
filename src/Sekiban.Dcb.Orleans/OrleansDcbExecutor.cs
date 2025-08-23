using Orleans;
using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.Orleans.Grains;
using System.Diagnostics;

namespace Sekiban.Dcb.Orleans;

/// <summary>
///     Orleans-specific implementation of ISekibanExecutor
///     Uses Orleans grains for distributed command execution and queries
/// </summary>
public class OrleansDcbExecutor : ISekibanExecutor
{
    private readonly IClusterClient _clusterClient;
    private readonly IActorObjectAccessor _actorAccessor;
    private readonly DcbDomainTypes _domainTypes;
    private readonly IEventStore _eventStore;
    private readonly GeneralSekibanExecutor _generalExecutor;

    public OrleansDcbExecutor(
        IClusterClient clusterClient,
        IEventStore eventStore,
        DcbDomainTypes domainTypes,
        IEventPublisher? eventPublisher = null)
    {
        _clusterClient = clusterClient ?? throw new ArgumentNullException(nameof(clusterClient));
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _domainTypes = domainTypes ?? throw new ArgumentNullException(nameof(domainTypes));
        _actorAccessor = new OrleansActorObjectAccessor(clusterClient, eventStore, domainTypes);
        _generalExecutor = new GeneralSekibanExecutor(eventStore, _actorAccessor, domainTypes, eventPublisher);
    }

    /// <summary>
    ///     Execute a command with its built-in handler
    /// </summary>
    public Task<ResultBox<ExecutionResult>> ExecuteAsync<TCommand>(
        TCommand command,
        CancellationToken cancellationToken = default) where TCommand : ICommandWithHandler<TCommand> =>
        _generalExecutor.ExecuteAsync(command, cancellationToken);

    /// <summary>
    ///     Execute a command with a handler function
    /// </summary>
    public Task<ResultBox<ExecutionResult>> ExecuteAsync<TCommand>(
        TCommand command,
        Func<TCommand, ICommandContext, Task<ResultBox<EventOrNone>>> handlerFunc,
        CancellationToken cancellationToken = default) where TCommand : ICommand =>
        _generalExecutor.ExecuteAsync(command, handlerFunc, cancellationToken);

    /// <summary>
    ///     Get the current state for a specific tag state
    /// </summary>
    public Task<ResultBox<TagState>> GetTagStateAsync(TagStateId tagStateId) =>
        _generalExecutor.GetTagStateAsync(tagStateId);
    
    /// <summary>
    ///     Execute a single-result query using Orleans grains
    /// </summary>
    public async Task<ResultBox<TResult>> QueryAsync<TResult>(IQueryCommon<TResult> queryCommon) 
        where TResult : notnull
    {
        try
        {
            // Get the multi-projector type for this query
            var projectorTypeResult = _domainTypes.QueryTypes.GetMultiProjectorType(queryCommon);
            if (!projectorTypeResult.IsSuccess)
            {
                return ResultBox.Error<TResult>(projectorTypeResult.GetException());
            }
            
            var projectorType = projectorTypeResult.GetValue();
            
            // Get the multi-projector name
            var projectorNameProperty = projectorType.GetProperty("MultiProjectorName");
            if (projectorNameProperty == null)
            {
                return ResultBox.Error<TResult>(
                    new InvalidOperationException($"Projector type {projectorType.Name} does not have MultiProjectorName property"));
            }
            
            var projectorName = projectorNameProperty.GetValue(null) as string;
            if (string.IsNullOrEmpty(projectorName))
            {
                return ResultBox.Error<TResult>(
                    new InvalidOperationException($"Projector type {projectorType.Name} has invalid MultiProjectorName"));
            }
            
            // Get the multi-projection grain directly
            var grain = _clusterClient.GetGrain<IMultiProjectionGrain>(projectorName);
            
            // Wait for sortable unique ID if needed
            await WaitForSortableUniqueIdIfNeeded(grain, queryCommon);
            
            // Execute the query on the grain
            var result = await grain.ExecuteQueryAsync(queryCommon);
            
            // Convert QueryResultGeneral back to typed result
            return result.ToTypedResult<TResult>();
        }
        catch (Exception ex)
        {
            return ResultBox.Error<TResult>(ex);
        }
    }
    
    /// <summary>
    ///     Execute a list query with pagination support using Orleans grains
    /// </summary>
    public async Task<ResultBox<ListQueryResult<TResult>>> QueryAsync<TResult>(IListQueryCommon<TResult> queryCommon)
        where TResult : notnull
    {
        try
        {
            // Get the multi-projector type for this query
            var projectorTypeResult = _domainTypes.QueryTypes.GetMultiProjectorType(queryCommon);
            if (!projectorTypeResult.IsSuccess)
            {
                return ResultBox.Error<ListQueryResult<TResult>>(projectorTypeResult.GetException());
            }
            
            var projectorType = projectorTypeResult.GetValue();
            
            // Get the multi-projector name
            var projectorNameProperty = projectorType.GetProperty("MultiProjectorName");
            if (projectorNameProperty == null)
            {
                return ResultBox.Error<ListQueryResult<TResult>>(
                    new InvalidOperationException($"Projector type {projectorType.Name} does not have MultiProjectorName property"));
            }
            
            var projectorName = projectorNameProperty.GetValue(null) as string;
            if (string.IsNullOrEmpty(projectorName))
            {
                return ResultBox.Error<ListQueryResult<TResult>>(
                    new InvalidOperationException($"Projector type {projectorType.Name} has invalid MultiProjectorName"));
            }
            
            // Get the multi-projection grain directly
            var grain = _clusterClient.GetGrain<IMultiProjectionGrain>(projectorName);
            
            // Wait for sortable unique ID if needed
            await WaitForSortableUniqueIdIfNeeded(grain, queryCommon);
            
            // Execute the list query on the grain
            var result = await grain.ExecuteListQueryAsync(queryCommon);
            
            // Convert ListQueryResultGeneral back to typed result
            return result.ToTypedResult<TResult>();
        }
        catch (Exception ex)
        {
            return ResultBox.Error<ListQueryResult<TResult>>(ex);
        }
    }
    
    /// <summary>
    /// Wait for a sortable unique ID to be processed if the query implements IWaitForSortableUniqueId
    /// </summary>
    private async Task WaitForSortableUniqueIdIfNeeded(IMultiProjectionGrain grain, object query)
    {
        if (query is IWaitForSortableUniqueId waitForQuery && 
            !string.IsNullOrEmpty(waitForQuery.WaitForSortableUniqueId))
        {
            var sortableUniqueId = waitForQuery.WaitForSortableUniqueId;
            
            // Calculate adaptive timeout based on the age of the sortable unique ID
            var timeoutMs = SortableUniqueIdWaitHelper.CalculateAdaptiveTimeout(sortableUniqueId);
            var pollingIntervalMs = SortableUniqueIdWaitHelper.DefaultPollingIntervalMs;
            
            var stopwatch = Stopwatch.StartNew();
            
            while (stopwatch.ElapsedMilliseconds < timeoutMs)
            {
                var isReceived = await grain.IsSortableUniqueIdReceived(sortableUniqueId);
                if (isReceived)
                {
                    return;
                }
                
                await Task.Delay(pollingIntervalMs);
            }
            
            // Timeout reached - we proceed with the query anyway
            // The query might return stale data, but that's better than failing completely
        }
    }
}