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
using Sekiban.Dcb.SizeGates;
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
        SekibanDcbCapabilityResolver.DescribeExecutor(_construction.ActorAccessor);

    private readonly OrleansDcbExecutorConstruction<GeneralSekibanExecutor> _construction;

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

    /// <summary>Additive opt-in size-gate constructor; existing Orleans construction remains ungated.</summary>
    public OrleansDcbExecutor(IClusterClient clusterClient, IEventStore eventStore, DcbDomainTypes domainTypes,
        ExecutorSizeGateOptions executorSizeGateOptions, IEventPublisher? eventPublisher = null,
        IServiceIdProvider? serviceIdProvider = null, IExecutedUserProvider? executedUserProvider = null)
        : this(CreateExceptionConstruction(new OrleansDcbExecutorConstructionInputs(
            clusterClient, eventStore, domainTypes, eventPublisher, serviceIdProvider, executedUserProvider,
            ProcessSharedSortableUniqueIdServices.Generator, ProcessSharedSortableUniqueIdServices.SeedCoordinator,
            SortableUniqueIdWaitPolicy.System, executorSizeGateOptions)))
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
        : this(CreateExceptionConstruction(new OrleansDcbExecutorConstructionInputs(
            clusterClient, eventStore, domainTypes, eventPublisher, serviceIdProvider, executedUserProvider,
            sortableUniqueIdGenerator, sortableUniqueIdSeedCoordinator, sortableUniqueIdWaitPolicy, null)))
    {
    }

    internal OrleansDcbExecutor(IClusterClient clusterClient, IEventStore eventStore, DcbDomainTypes domainTypes,
        IEventPublisher? eventPublisher, IServiceIdProvider? serviceIdProvider, IExecutedUserProvider? executedUserProvider,
        ISortableUniqueIdGenerator sortableUniqueIdGenerator, SortableUniqueIdSeedCoordinator sortableUniqueIdSeedCoordinator,
        SortableUniqueIdWaitPolicy sortableUniqueIdWaitPolicy, ExecutorSizeGateOptions? executorSizeGateOptions)
        : this(CreateExceptionConstruction(new OrleansDcbExecutorConstructionInputs(
            clusterClient, eventStore, domainTypes, eventPublisher, serviceIdProvider, executedUserProvider,
            sortableUniqueIdGenerator, sortableUniqueIdSeedCoordinator, sortableUniqueIdWaitPolicy,
            executorSizeGateOptions)))
    {
    }

    private OrleansDcbExecutor(OrleansDcbExecutorConstruction<GeneralSekibanExecutor> construction) =>
        _construction = construction;

    private static OrleansDcbExecutorConstruction<GeneralSekibanExecutor> CreateExceptionConstruction(
        OrleansDcbExecutorConstructionInputs inputs) =>
        OrleansDcbExecutorConstruction<GeneralSekibanExecutor>.Create(inputs, CreateExceptionGeneralExecutor);

    private static GeneralSekibanExecutor CreateExceptionGeneralExecutor(
        OrleansDcbExecutorConstructionInputs inputs,
        IActorObjectAccessor actorAccessor,
        IServiceIdProvider serviceIdProvider,
        SortableUniqueIdWaitPolicy sortableUniqueIdWaitPolicy) =>
        new(
            inputs.EventStore,
            actorAccessor,
            inputs.DomainTypes,
            inputs.EventPublisher,
            inputs.ExecutedUserProvider,
            inputs.SortableUniqueIdGenerator,
            inputs.SortableUniqueIdSeedCoordinator,
            serviceIdProvider,
            sortableUniqueIdWaitPolicy,
            inputs.ExecutorSizeGateOptions);

    /// <summary>
    ///     Execute a command with its built-in handler
    /// </summary>
    public Task<ExecutionResult> ExecuteAsync<TCommand>(
        TCommand command,
        CancellationToken cancellationToken = default) where TCommand : ICommandWithHandler<TCommand> =>
        _construction.GeneralExecutor.ExecuteAsync(command, cancellationToken);

    /// <summary>
    ///     Execute a command with a handler function
    /// </summary>
    public Task<ExecutionResult> ExecuteAsync<TCommand>(
        TCommand command,
        Func<TCommand, ICommandContext, Task<EventOrNone>> handlerFunc,
        CancellationToken cancellationToken = default) where TCommand : ICommand =>
        _construction.GeneralExecutor.ExecuteAsync(command, handlerFunc, cancellationToken);

    /// <summary>
    ///     Execute a handler function without an explicit command
    /// </summary>
    public Task<ExecutionResult> ExecuteCommandAsync(
        Func<ICommandContext, Task<EventOrNone>> handlerFunc,
        CancellationToken cancellationToken = default) =>
        _construction.GeneralExecutor.ExecuteCommandAsync(handlerFunc, cancellationToken);

    /// <summary>
    ///     Get the current state for a specific tag state
    /// </summary>
    public Task<TagState> GetTagStateAsync(TagStateId tagStateId) =>
        _construction.GeneralExecutor.GetTagStateAsync(tagStateId);

    public Task<TagState> GetTagStateAsync(TagStateId tagStateId, CancellationToken cancellationToken) =>
        _construction.GeneralExecutor.GetTagStateAsync(tagStateId, cancellationToken);

    /// <summary>
    ///     Execute a single-result query using Orleans grains
    /// </summary>
    public async Task<TResult> QueryAsync<TResult>(IQueryCommon<TResult> queryCommon) where TResult : notnull
    {
        var projectorName = ResolveProjectorName(queryCommon);

        var result = await _construction.QueryExecutor.ExecuteQueryAsync(
            queryCommon,
            projectorName,
            SortableUniqueIdWaitSurface.OrleansWithoutResultSingle);

        return await DeserializeQueryResultAsync<TResult>(result);
    }

    /// <summary>
    ///     Execute a list query with pagination support using Orleans grains
    /// </summary>
    public async Task<ListQueryResult<TResult>> QueryAsync<TResult>(IListQueryCommon<TResult> queryCommon)
        where TResult : notnull
    {
        var projectorName = ResolveProjectorName(queryCommon);

        var result = await _construction.QueryExecutor.ExecuteListQueryAsync(
            queryCommon,
            projectorName,
            SortableUniqueIdWaitSurface.OrleansWithoutResultList);

        return await DeserializeListQueryResultAsync<TResult>(result);
    }

    private async Task<TResult> DeserializeQueryResultAsync<TResult>(
        SerializableQueryResult result)
        where TResult : notnull
    {
        var context = new BoundaryContext("ISekibanExecutor.QueryAsync", typeof(TResult).Name);
        var general = await GuardedUnwrap.UnwrapAsync(result.ToQueryResultAsync(_construction.DomainTypes), context);
        return GuardedUnwrap.Unwrap(general.ToTypedResult<TResult>(), context);
    }

    private async Task<ListQueryResult<TResult>> DeserializeListQueryResultAsync<TResult>(
        SerializableListQueryResult result)
        where TResult : notnull
    {
        var context = new BoundaryContext("ISekibanExecutor.QueryAsync (list)", typeof(TResult).Name);
        var listGeneral = await GuardedUnwrap.UnwrapAsync(result.ToListQueryResultAsync(_construction.DomainTypes), context);
        return GuardedUnwrap.Unwrap(listGeneral.ToTypedResult<TResult>(), context);
    }

    public Task<string> GetLatestSortableUniqueIdAsync() =>
        _construction.GeneralExecutor.GetLatestSortableUniqueIdAsync();

    public async Task<ProjectionHeadStatus> GetProjectionHeadStatusAsync(
        string projectorName,
        string? expectedProjectorVersion = null)
    {
        var projectorVersionResult = ProjectionHeadStatusUtilities.ValidateProjectorVersion(
            _construction.DomainTypes,
            projectorName,
            expectedProjectorVersion);
        if (!projectorVersionResult.IsSuccess)
        {
            throw projectorVersionResult.GetException();
        }

        var grainId = ServiceIdGrainKey.Build(_construction.ServiceIdProvider.GetCurrentServiceId(), projectorName);
        var grain = _construction.ClusterClient.GetGrain<IMultiProjectionGrain>(grainId);
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
        _construction.GeneralExecutor.GetEventStoreHeadStatusAsync(includeTotalEventCount);

    public Task<ResultBox<SerializableTagState>> GetSerializableTagStateAsync(TagStateId tagStateId) =>
        _construction.GeneralExecutor.GetSerializableTagStateAsync(tagStateId);

    public Task<ResultBox<SerializedCommitResult>> CommitSerializableEventsAsync(
        SerializedCommitRequest request,
        CancellationToken cancellationToken = default) =>
        _construction.GeneralExecutor.CommitSerializableEventsAsync(request, cancellationToken);

    /// <summary>Forwards the additive V2 serialized expected-head contract to the common executor/store path.</summary>
    public Task<ResultBox<SerializedCommitResult>> CommitSerializableEventsWithExpectedTagPositionsAsync(
        VersionedExpectedTagPositionSerializedCommitRequest request,
        CancellationToken cancellationToken = default) =>
        _construction.GeneralExecutor.CommitSerializableEventsWithExpectedTagPositionsAsync(request, cancellationToken);

    private string ResolveProjectorName(IQueryCommon queryCommon)
    {
        var projectorTypeResult = _construction.DomainTypes.QueryTypes.GetMultiProjectorType(queryCommon);
        var projectorNameResult = ProjectionHeadStatusUtilities.ResolveProjectorName(projectorTypeResult);
        if (!projectorNameResult.IsSuccess)
        {
            throw projectorNameResult.GetException();
        }

        return projectorNameResult.GetValue();
    }

    private string ResolveProjectorName(IListQueryCommon queryCommon)
    {
        var projectorTypeResult = _construction.DomainTypes.QueryTypes.GetMultiProjectorType(queryCommon);
        var projectorNameResult = ProjectionHeadStatusUtilities.ResolveProjectorName(projectorTypeResult);
        if (!projectorNameResult.IsSuccess)
        {
            throw projectorNameResult.GetException();
        }

        return projectorNameResult.GetValue();
    }
}
