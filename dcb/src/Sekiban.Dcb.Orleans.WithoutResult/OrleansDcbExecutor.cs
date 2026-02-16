using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Orleans.Grains;
using Sekiban.Dcb.Orleans.ServiceId;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using System.Diagnostics;
using Sekiban.Dcb.Orleans.Serialization;
namespace Sekiban.Dcb.Orleans;

/// <summary>
///     Orleans-specific implementation of ISekibanExecutor (exception-based)
///     Uses Orleans grains for distributed command execution and queries
/// </summary>
public class OrleansDcbExecutor : ISekibanExecutor, ISerializedSekibanDcbExecutor
{
    private readonly IActorObjectAccessor _actorAccessor;
    private readonly IClusterClient _clusterClient;
    private readonly DcbDomainTypes _domainTypes;
    private readonly IEventStore _eventStore;
    private readonly GeneralSekibanExecutor _generalExecutor;
    private readonly IServiceIdProvider _serviceIdProvider;

    public OrleansDcbExecutor(
        IClusterClient clusterClient,
        IEventStore eventStore,
        DcbDomainTypes domainTypes,
        IEventPublisher? eventPublisher = null,
        IServiceIdProvider? serviceIdProvider = null)
    {
        _clusterClient = clusterClient ?? throw new ArgumentNullException(nameof(clusterClient));
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _domainTypes = domainTypes ?? throw new ArgumentNullException(nameof(domainTypes));
        _serviceIdProvider = serviceIdProvider ?? new DefaultServiceIdProvider();
        _actorAccessor = new OrleansActorObjectAccessor(clusterClient, eventStore, domainTypes, _serviceIdProvider);
        _generalExecutor = new GeneralSekibanExecutor(eventStore, _actorAccessor, domainTypes, eventPublisher);
    }

    /// <summary>
    ///     Execute a command with its built-in handler
    /// </summary>
    public Task<ExecutionResult> ExecuteAsync<TCommand>(
        TCommand command,
        CancellationToken cancellationToken = default) where TCommand : ICommandWithHandler<TCommand> =>
        _generalExecutor.ExecuteAsync(command, cancellationToken);

    /// <summary>
    ///     Execute a command with a handler function
    /// </summary>
    public Task<ExecutionResult> ExecuteAsync<TCommand>(
        TCommand command,
        Func<TCommand, ICommandContext, Task<EventOrNone>> handlerFunc,
        CancellationToken cancellationToken = default) where TCommand : ICommand =>
        _generalExecutor.ExecuteAsync(command, handlerFunc, cancellationToken);

    /// <summary>
    ///     Execute a handler function without an explicit command
    /// </summary>
    public Task<ExecutionResult> ExecuteCommandAsync(
        Func<ICommandContext, Task<EventOrNone>> handlerFunc,
        CancellationToken cancellationToken = default) =>
        _generalExecutor.ExecuteCommandAsync(handlerFunc, cancellationToken);

    /// <summary>
    ///     Get the current state for a specific tag state
    /// </summary>
    public Task<TagState> GetTagStateAsync(TagStateId tagStateId) =>
        _generalExecutor.GetTagStateAsync(tagStateId);

    /// <summary>
    ///     Execute a single-result query using Orleans grains
    /// </summary>
    public async Task<TResult> QueryAsync<TResult>(IQueryCommon<TResult> queryCommon) where TResult : notnull
    {
        // Get the multi-projector type for this query
        var projectorTypeResult = _domainTypes.QueryTypes.GetMultiProjectorType(queryCommon);
        if (!projectorTypeResult.IsSuccess)
        {
            throw projectorTypeResult.GetException();
        }

        var projectorType = projectorTypeResult.GetValue();

        // Get the multi-projector name
        var projectorNameProperty = projectorType.GetProperty("MultiProjectorName");
        if (projectorNameProperty == null)
        {
            throw new InvalidOperationException(
                $"Projector type {projectorType.Name} does not have MultiProjectorName property");
        }

        var projectorName = projectorNameProperty.GetValue(null) as string;
        if (string.IsNullOrEmpty(projectorName))
        {
            throw new InvalidOperationException(
                $"Projector type {projectorType.Name} has invalid MultiProjectorName");
        }

        // Get the multi-projection grain directly
        var grainId = ServiceIdGrainKey.Build(_serviceIdProvider.GetCurrentServiceId(), projectorName);
        var grain = _clusterClient.GetGrain<IMultiProjectionGrain>(grainId);

        // Wait for sortable unique ID if needed
        await WaitForSortableUniqueIdIfNeeded(grain, queryCommon);

        var serializableQuery = await SerializableQueryParameter.CreateFromAsync(
            queryCommon,
            _domainTypes.JsonSerializerOptions);

        var result = await grain.ExecuteQueryAsync(serializableQuery);

        return await DeserializeQueryResultAsync<TResult>(result);
    }

    /// <summary>
    ///     Execute a list query with pagination support using Orleans grains
    /// </summary>
    public async Task<ListQueryResult<TResult>> QueryAsync<TResult>(IListQueryCommon<TResult> queryCommon)
        where TResult : notnull
    {
        // Get the multi-projector type for this query
        var projectorTypeResult = _domainTypes.QueryTypes.GetMultiProjectorType(queryCommon);
        if (!projectorTypeResult.IsSuccess)
        {
            throw projectorTypeResult.GetException();
        }

        var projectorType = projectorTypeResult.GetValue();

        // Get the multi-projector name
        var projectorNameProperty = projectorType.GetProperty("MultiProjectorName");
        if (projectorNameProperty == null)
        {
            throw new InvalidOperationException(
                $"Projector type {projectorType.Name} does not have MultiProjectorName property");
        }

        var projectorName = projectorNameProperty.GetValue(null) as string;
        if (string.IsNullOrEmpty(projectorName))
        {
            throw new InvalidOperationException(
                $"Projector type {projectorType.Name} has invalid MultiProjectorName");
        }

        // Get the multi-projection grain directly
        var grainId = ServiceIdGrainKey.Build(_serviceIdProvider.GetCurrentServiceId(), projectorName);
        var grain = _clusterClient.GetGrain<IMultiProjectionGrain>(grainId);

        // Wait for sortable unique ID if needed
        await WaitForSortableUniqueIdIfNeeded(grain, queryCommon);

        var serializableQuery = await SerializableQueryParameter.CreateFromAsync(
            queryCommon,
            _domainTypes.JsonSerializerOptions);

        var result = await grain.ExecuteListQueryAsync(serializableQuery);

        return await DeserializeListQueryResultAsync<TResult>(result);
    }

    /// <summary>
    ///     Wait for a sortable unique ID to be processed if the query implements IWaitForSortableUniqueId
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

    private async Task<TResult> DeserializeQueryResultAsync<TResult>(
        SerializableQueryResult result)
        where TResult : notnull
    {
        var generalBox = await result.ToQueryResultAsync(_domainTypes);
        if (!generalBox.IsSuccess)
        {
            throw generalBox.GetException();
        }

        return generalBox.GetValue().ToTypedResult<TResult>().UnwrapBox();
    }

    private async Task<ListQueryResult<TResult>> DeserializeListQueryResultAsync<TResult>(
        SerializableListQueryResult result)
        where TResult : notnull
    {
        var listGeneralBox = await result.ToListQueryResultAsync(_domainTypes);
        if (!listGeneralBox.IsSuccess)
        {
            throw listGeneralBox.GetException();
        }

        return listGeneralBox.GetValue().ToTypedResult<TResult>().UnwrapBox();
    }

    public Task<ResultBox<SerializableTagState>> GetSerializableTagStateAsync(TagStateId tagStateId) =>
        _generalExecutor.GetSerializableTagStateAsync(tagStateId);

    public Task<ResultBox<SerializedCommitResult>> CommitSerializableEventsAsync(
        SerializedCommitRequest request,
        CancellationToken cancellationToken = default) =>
        _generalExecutor.CommitSerializableEventsAsync(request, cancellationToken);
}
