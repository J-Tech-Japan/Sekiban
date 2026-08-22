using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Capabilities;
using Sekiban.Dcb.Boundaries;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Orleans.Grains;
using Sekiban.Dcb.Orleans.ServiceId;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
using Sekiban.Dcb.Orleans.Serialization;
namespace Sekiban.Dcb.Orleans;

/// <summary>
///     Orleans-specific implementation of ISekibanExecutor (exception-based)
///     Uses Orleans grains for distributed command execution and queries
/// </summary>
public class OrleansDcbExecutor : ISekibanExecutor, ISerializedSekibanDcbExecutor,
    ISerializedExpectedTagPositionSekibanDcbExecutor, IExecutorRuntimeDescriptorProvider
{
    /// <summary>Commands are executed by Orleans grains across the cluster.</summary>
    public ExecutorRuntimeDescriptor DescribeRuntime() =>
        SekibanDcbCapabilityResolver.DescribeExecutor(_actorAccessor);

    private readonly IActorObjectAccessor _actorAccessor;
    private readonly IClusterClient _clusterClient;
    private readonly DcbDomainTypes _domainTypes;
    private readonly IEventStore _eventStore;
    private readonly GeneralSekibanExecutor _generalExecutor;
    private readonly IServiceIdProvider _serviceIdProvider;
    private readonly SortableUniqueIdWaitPolicy _sortableUniqueIdWaitPolicy;

    /// <summary>
    ///     Binary-compatible overload preserved for callers compiled against the pre-SEK-G23 constructor.
    /// </summary>
    public OrleansDcbExecutor(
        IClusterClient clusterClient,
        IEventStore eventStore,
        DcbDomainTypes domainTypes,
        IEventPublisher? eventPublisher,
        IServiceIdProvider? serviceIdProvider)
        : this(clusterClient, eventStore, domainTypes, eventPublisher, serviceIdProvider, null)
    {
    }

    public OrleansDcbExecutor(
        IClusterClient clusterClient,
        IEventStore eventStore,
        DcbDomainTypes domainTypes,
        IEventPublisher? eventPublisher = null,
        IServiceIdProvider? serviceIdProvider = null,
        IExecutedUserProvider? executedUserProvider = null)
        : this(
            clusterClient,
            eventStore,
            domainTypes,
            eventPublisher,
            serviceIdProvider,
            executedUserProvider,
            ProcessSharedSortableUniqueIdServices.Generator,
            ProcessSharedSortableUniqueIdServices.SeedCoordinator,
            SortableUniqueIdWaitPolicy.System)
    {
    }

    /// <summary>Creates an Orleans executor using the registered process-wide monotonic id allocator.</summary>
    public OrleansDcbExecutor(
        IClusterClient clusterClient,
        IEventStore eventStore,
        DcbDomainTypes domainTypes,
        IEventPublisher? eventPublisher,
        IServiceIdProvider? serviceIdProvider,
        IExecutedUserProvider? executedUserProvider,
        ISortableUniqueIdGenerator sortableUniqueIdGenerator,
        SortableUniqueIdSeedCoordinator sortableUniqueIdSeedCoordinator)
        : this(
            clusterClient,
            eventStore,
            domainTypes,
            eventPublisher,
            serviceIdProvider,
            executedUserProvider,
            sortableUniqueIdGenerator,
            sortableUniqueIdSeedCoordinator,
            SortableUniqueIdWaitPolicy.System)
    {
    }

    internal OrleansDcbExecutor(
        IClusterClient clusterClient,
        IEventStore eventStore,
        DcbDomainTypes domainTypes,
        IEventPublisher? eventPublisher,
        IServiceIdProvider? serviceIdProvider,
        IExecutedUserProvider? executedUserProvider,
        ISortableUniqueIdGenerator sortableUniqueIdGenerator,
        SortableUniqueIdSeedCoordinator sortableUniqueIdSeedCoordinator,
        SortableUniqueIdWaitPolicy sortableUniqueIdWaitPolicy)
    {
        _clusterClient = clusterClient ?? throw new ArgumentNullException(nameof(clusterClient));
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _domainTypes = domainTypes ?? throw new ArgumentNullException(nameof(domainTypes));
        _serviceIdProvider = serviceIdProvider ?? new DefaultServiceIdProvider();
        _sortableUniqueIdWaitPolicy = sortableUniqueIdWaitPolicy ??
                                      throw new ArgumentNullException(nameof(sortableUniqueIdWaitPolicy));
        _actorAccessor = new OrleansActorObjectAccessor(clusterClient, eventStore, domainTypes, _serviceIdProvider);
        _generalExecutor = new GeneralSekibanExecutor(
            eventStore,
            _actorAccessor,
            domainTypes,
            eventPublisher,
            executedUserProvider,
            sortableUniqueIdGenerator,
            sortableUniqueIdSeedCoordinator,
            _serviceIdProvider,
            _sortableUniqueIdWaitPolicy);
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
        var projectorName = ResolveProjectorName(queryCommon);

        // Get the multi-projection grain directly
        var grainId = ServiceIdGrainKey.Build(_serviceIdProvider.GetCurrentServiceId(), projectorName);
        var grain = _clusterClient.GetGrain<IMultiProjectionGrain>(grainId);

        // Wait for sortable unique ID if needed
        await WaitForSortableUniqueIdIfNeeded(
            grain,
            queryCommon,
            SortableUniqueIdWaitSurface.OrleansWithoutResultSingle);

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
        var projectorName = ResolveProjectorName(queryCommon);

        // Get the multi-projection grain directly
        var grainId = ServiceIdGrainKey.Build(_serviceIdProvider.GetCurrentServiceId(), projectorName);
        var grain = _clusterClient.GetGrain<IMultiProjectionGrain>(grainId);

        // Wait for sortable unique ID if needed
        await WaitForSortableUniqueIdIfNeeded(
            grain,
            queryCommon,
            SortableUniqueIdWaitSurface.OrleansWithoutResultList);

        var serializableQuery = await SerializableQueryParameter.CreateFromAsync(
            queryCommon,
            _domainTypes.JsonSerializerOptions);

        var result = await grain.ExecuteListQueryAsync(serializableQuery);

        return await DeserializeListQueryResultAsync<TResult>(result);
    }

    /// <summary>
    ///     Wait for a sortable unique ID to be processed if the query implements IWaitForSortableUniqueId.
    ///     Strict marker queries fail before serialization when the wait times out; legacy queries keep fail-open.
    /// </summary>
    private async Task WaitForSortableUniqueIdIfNeeded(
        IMultiProjectionGrain grain,
        object query,
        SortableUniqueIdWaitSurface surface)
    {
        if (query is not IWaitForSortableUniqueId waitForQuery ||
            string.IsNullOrEmpty(waitForQuery.WaitForSortableUniqueId))
        {
            return;
        }

        var sortableUniqueId = waitForQuery.WaitForSortableUniqueId;
        var strict = query is IStrictWaitForSortableUniqueId;
        var wait = await _sortableUniqueIdWaitPolicy.WaitAsync(
            sortableUniqueId,
            surface,
            strict ? SortableUniqueIdWaitMode.Strict : SortableUniqueIdWaitMode.Legacy,
            cancellationToken => ProbeSortableUniqueIdAsync(grain, sortableUniqueId, cancellationToken),
            strict
                ? cancellationToken => ReadCurrentSortableUniqueIdAsync(grain, cancellationToken)
                : null);

        if (strict && wait.TimedOut)
        {
            throw new SortableUniqueIdWaitTimeoutException(
                sortableUniqueId,
                wait.Timeout,
                wait.Elapsed,
                wait.LastObservedSortableUniqueId);
        }
    }

    private static async Task<bool> ProbeSortableUniqueIdAsync(
        IMultiProjectionGrain grain,
        string sortableUniqueId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await grain.IsSortableUniqueIdReceived(sortableUniqueId).ConfigureAwait(false);
    }

    private static async Task<string?> ReadCurrentSortableUniqueIdAsync(
        IMultiProjectionGrain grain,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var status = await grain.GetProjectionHeadStatusAsync().ConfigureAwait(false);
        return status.CurrentLastSortableUniqueId;
    }

    private async Task<TResult> DeserializeQueryResultAsync<TResult>(
        SerializableQueryResult result)
        where TResult : notnull
    {
        var context = new BoundaryContext("ISekibanExecutor.QueryAsync", typeof(TResult).Name);
        var general = await GuardedUnwrap.UnwrapAsync(result.ToQueryResultAsync(_domainTypes), context);
        return GuardedUnwrap.Unwrap(general.ToTypedResult<TResult>(), context);
    }

    private async Task<ListQueryResult<TResult>> DeserializeListQueryResultAsync<TResult>(
        SerializableListQueryResult result)
        where TResult : notnull
    {
        var context = new BoundaryContext("ISekibanExecutor.QueryAsync (list)", typeof(TResult).Name);
        var listGeneral = await GuardedUnwrap.UnwrapAsync(result.ToListQueryResultAsync(_domainTypes), context);
        return GuardedUnwrap.Unwrap(listGeneral.ToTypedResult<TResult>(), context);
    }

    public Task<string> GetLatestSortableUniqueIdAsync() =>
        _generalExecutor.GetLatestSortableUniqueIdAsync();

    public async Task<ProjectionHeadStatus> GetProjectionHeadStatusAsync(
        string projectorName,
        string? expectedProjectorVersion = null)
    {
        var projectorVersionResult = ProjectionHeadStatusUtilities.ValidateProjectorVersion(
            _domainTypes,
            projectorName,
            expectedProjectorVersion);
        if (!projectorVersionResult.IsSuccess)
        {
            throw projectorVersionResult.GetException();
        }

        var grainId = ServiceIdGrainKey.Build(_serviceIdProvider.GetCurrentServiceId(), projectorName);
        var grain = _clusterClient.GetGrain<IMultiProjectionGrain>(grainId);
        var grainStatus = await grain.GetProjectionHeadStatusAsync();

        var projectorNameResult = ProjectionHeadStatusUtilities.EnsureProjectorNameConsistency(
            projectorName,
            grainStatus.ProjectorName);
        if (!projectorNameResult.IsSuccess)
        {
            throw projectorNameResult.GetException();
        }

        var projectorVersionConsistencyResult = ProjectionHeadStatusUtilities.EnsureProjectorVersionConsistency(
            projectorVersionResult.GetValue(),
            grainStatus.ProjectorVersion);
        if (!projectorVersionConsistencyResult.IsSuccess)
        {
            throw projectorVersionConsistencyResult.GetException();
        }

        return new ProjectionHeadStatus(
            projectorNameResult.GetValue(),
            projectorVersionConsistencyResult.GetValue(),
            new ProjectionPosition(
                grainStatus.CurrentEventVersion,
                ProjectionHeadStatusUtilities.NormalizeSortableUniqueId(grainStatus.CurrentLastSortableUniqueId)),
            new ProjectionPosition(
                grainStatus.ConsistentEventVersion,
                ProjectionHeadStatusUtilities.NormalizeSortableUniqueId(grainStatus.ConsistentLastSortableUniqueId)),
            new ProjectionCatchUpStatus(
                grainStatus.IsCatchUpInProgress,
                ProjectionHeadStatusUtilities.NormalizeSortableUniqueId(grainStatus.CatchUpCurrentSortableUniqueId),
                ProjectionHeadStatusUtilities.NormalizeSortableUniqueId(grainStatus.CatchUpTargetSortableUniqueId),
                grainStatus.PendingStreamEventCount));
    }

    public Task<EventStoreHeadStatus> GetEventStoreHeadStatusAsync(bool includeTotalEventCount = false) =>
        _generalExecutor.GetEventStoreHeadStatusAsync(includeTotalEventCount);

    public Task<ResultBox<SerializableTagState>> GetSerializableTagStateAsync(TagStateId tagStateId) =>
        _generalExecutor.GetSerializableTagStateAsync(tagStateId);

    public Task<ResultBox<SerializedCommitResult>> CommitSerializableEventsAsync(
        SerializedCommitRequest request,
        CancellationToken cancellationToken = default) =>
        _generalExecutor.CommitSerializableEventsAsync(request, cancellationToken);

    /// <summary>Forwards the additive V2 serialized expected-head contract to the common executor/store path.</summary>
    public Task<ResultBox<SerializedCommitResult>> CommitSerializableEventsWithExpectedTagPositionsAsync(
        VersionedExpectedTagPositionSerializedCommitRequest request,
        CancellationToken cancellationToken = default) =>
        _generalExecutor.CommitSerializableEventsWithExpectedTagPositionsAsync(request, cancellationToken);

    private string ResolveProjectorName(IQueryCommon queryCommon)
    {
        var projectorTypeResult = _domainTypes.QueryTypes.GetMultiProjectorType(queryCommon);
        var projectorNameResult = ProjectionHeadStatusUtilities.ResolveProjectorName(projectorTypeResult);
        if (!projectorNameResult.IsSuccess)
        {
            throw projectorNameResult.GetException();
        }

        return projectorNameResult.GetValue();
    }

    private string ResolveProjectorName(IListQueryCommon queryCommon)
    {
        var projectorTypeResult = _domainTypes.QueryTypes.GetMultiProjectorType(queryCommon);
        var projectorNameResult = ProjectionHeadStatusUtilities.ResolveProjectorName(projectorTypeResult);
        if (!projectorNameResult.IsSuccess)
        {
            throw projectorNameResult.GetException();
        }

        return projectorNameResult.GetValue();
    }
}
