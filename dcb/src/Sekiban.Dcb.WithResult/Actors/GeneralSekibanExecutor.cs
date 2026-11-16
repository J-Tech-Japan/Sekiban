using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Capabilities;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.SizeGates;
using Sekiban.Dcb.TagConsistencyFence;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.Actors;

/// <summary>
///     ResultBox-based implementation of ISekibanExecutor
///     Wraps CoreGeneralSekibanExecutor to provide the public API
///     This implementation uses ResultBox for all error handling
/// </summary>
public class GeneralSekibanExecutor : ISekibanExecutor, ISerializedSekibanDcbExecutor, IExecutorRuntimeDescriptorProvider,
    IConditionalCommandExecutor, ISerializedConditionalSekibanDcbExecutor, ISerializedExpectedTagPositionSekibanDcbExecutor
{
    private readonly CoreGeneralSekibanExecutor _core;
    private readonly IActorObjectAccessor _actorAccessor;
    private static readonly AnonymousCommand NoCommandInstance = new();

    /// <summary>
    ///     Binary-compatible overload preserved for callers compiled against the pre-SEK-G23 constructor.
    /// </summary>
    public GeneralSekibanExecutor(
        IEventStore eventStore,
        IActorObjectAccessor actorAccessor,
        DcbDomainTypes domainTypes,
        IEventPublisher? eventPublisher)
        : this(eventStore, actorAccessor, domainTypes, eventPublisher, null)
    {
    }

    public GeneralSekibanExecutor(
        IEventStore eventStore,
        IActorObjectAccessor actorAccessor,
        DcbDomainTypes domainTypes,
        IEventPublisher? eventPublisher = null,
        IExecutedUserProvider? executedUserProvider = null)
    {
        _actorAccessor = actorAccessor;
        _core = new CoreGeneralSekibanExecutor(eventStore, actorAccessor, domainTypes, eventPublisher, executedUserProvider);
    }

    /// <summary>Additive opt-in size-gate constructor; legacy construction remains ungated.</summary>
    public GeneralSekibanExecutor(IEventStore eventStore, IActorObjectAccessor actorAccessor, DcbDomainTypes domainTypes,
        ExecutorSizeGateOptions executorSizeGateOptions, IEventPublisher? eventPublisher = null,
        IExecutedUserProvider? executedUserProvider = null)
        : this(CreateResultConstruction(
            actorAccessor,
            () => CoreGeneralSekibanExecutorFactory.CreateWithGate(
                eventStore, actorAccessor, domainTypes, executorSizeGateOptions, eventPublisher, executedUserProvider)))
    {
    }

    /// <summary>Creates an executor using the registered process-wide monotonic id allocator.</summary>
    public GeneralSekibanExecutor(
        IEventStore eventStore,
        IActorObjectAccessor actorAccessor,
        DcbDomainTypes domainTypes,
        IEventPublisher? eventPublisher,
        IExecutedUserProvider? executedUserProvider,
        ISortableUniqueIdGenerator sortableUniqueIdGenerator,
        SortableUniqueIdSeedCoordinator sortableUniqueIdSeedCoordinator,
        IServiceIdProvider serviceIdProvider)
        : this(
            eventStore,
            actorAccessor,
            domainTypes,
            eventPublisher,
            executedUserProvider,
            sortableUniqueIdGenerator,
            sortableUniqueIdSeedCoordinator,
            serviceIdProvider,
            SortableUniqueIdWaitPolicy.System)
    {
    }

    internal GeneralSekibanExecutor(
        IEventStore eventStore,
        IActorObjectAccessor actorAccessor,
        DcbDomainTypes domainTypes,
        IEventPublisher? eventPublisher,
        IExecutedUserProvider? executedUserProvider,
        ISortableUniqueIdGenerator sortableUniqueIdGenerator,
        SortableUniqueIdSeedCoordinator sortableUniqueIdSeedCoordinator,
        IServiceIdProvider serviceIdProvider,
        SortableUniqueIdWaitPolicy sortableUniqueIdWaitPolicy)
        : this(CreateResultConstruction(
            actorAccessor,
            () => CoreGeneralSekibanExecutorFactory.CreateWithServices(
                eventStore, actorAccessor, domainTypes, eventPublisher, executedUserProvider,
                sortableUniqueIdGenerator, sortableUniqueIdSeedCoordinator, serviceIdProvider,
                sortableUniqueIdWaitPolicy, null)))
    {
    }

    internal GeneralSekibanExecutor(IEventStore eventStore, IActorObjectAccessor actorAccessor, DcbDomainTypes domainTypes,
        IEventPublisher? eventPublisher, IExecutedUserProvider? executedUserProvider,
        ISortableUniqueIdGenerator sortableUniqueIdGenerator, SortableUniqueIdSeedCoordinator sortableUniqueIdSeedCoordinator,
        IServiceIdProvider serviceIdProvider, SortableUniqueIdWaitPolicy sortableUniqueIdWaitPolicy,
        ExecutorSizeGateOptions? executorSizeGateOptions)
        : this(CreateResultConstruction(
            actorAccessor,
            () => CoreGeneralSekibanExecutorFactory.CreateWithServices(
                eventStore, actorAccessor, domainTypes, eventPublisher, executedUserProvider,
                sortableUniqueIdGenerator, sortableUniqueIdSeedCoordinator, serviceIdProvider,
                sortableUniqueIdWaitPolicy, executorSizeGateOptions)))
    {
    }

    public GeneralSekibanExecutor(
        IEventStore eventStore, IActorObjectAccessor actorAccessor, DcbDomainTypes domainTypes,
        TagConsistencyFenceOptions tagConsistencyFenceOptions, ExecutorSizeGateOptions? executorSizeGateOptions = null,
        IEventPublisher? eventPublisher = null, IExecutedUserProvider? executedUserProvider = null,
        IServiceIdProvider? serviceIdProvider = null)
        : this(GeneralSekibanExecutorConstruction.WithFence(
            eventStore, actorAccessor, domainTypes, tagConsistencyFenceOptions, executorSizeGateOptions,
            eventPublisher, executedUserProvider, serviceIdProvider))
    {
    }

    public GeneralSekibanExecutor(
        IEventStore eventStore, IActorObjectAccessor actorAccessor, DcbDomainTypes domainTypes,
        IEventPublisher? eventPublisher, IExecutedUserProvider? executedUserProvider,
        ISortableUniqueIdGenerator sortableUniqueIdGenerator, SortableUniqueIdSeedCoordinator sortableUniqueIdSeedCoordinator,
        IServiceIdProvider serviceIdProvider, ExecutorSizeGateOptions? executorSizeGateOptions = null,
        TagConsistencyFenceOptions? tagConsistencyFenceOptions = null)
        : this(GeneralSekibanExecutorConstruction.WithFence(
            eventStore, actorAccessor, domainTypes, tagConsistencyFenceOptions, executorSizeGateOptions,
            eventPublisher, executedUserProvider, serviceIdProvider,
            sortableUniqueIdGenerator, sortableUniqueIdSeedCoordinator))
    {
    }

    internal GeneralSekibanExecutor(GeneralSekibanExecutorConstruction construction)
    {
        _actorAccessor = construction.ActorAccessor;
        _core = construction.Core;
    }

    private static GeneralSekibanExecutorConstruction CreateResultConstruction(
        IActorObjectAccessor actorAccessor,
        Func<CoreGeneralSekibanExecutor> createCore) =>
        new(actorAccessor, createCore());

    /// <summary>
    ///     This executor is whatever its accessor is. It has no runtime of its own — commands go wherever the accessor
    ///     sends them — so it asks, rather than claiming. An accessor that will not say leaves this Unknown, and the
    ///     production guard treats Unknown as unsafe, which is the only safe way to treat it.
    /// </summary>
    public ExecutorRuntimeDescriptor DescribeRuntime() =>
        SekibanDcbCapabilityResolver.DescribeExecutor(_actorAccessor);

    /// <summary>
    ///     Execute a command with its built-in handler
    /// </summary>
    public Task<ResultBox<ExecutionResult>> ExecuteAsync<TCommand>(
        TCommand command,
        CancellationToken cancellationToken = default) where TCommand : ICommandWithHandler<TCommand> =>
        ResultBoxBridge.CaptureAsync(_core.ExecuteAsync(command, CoreHandler<TCommand>(), cancellationToken));

    /// <summary>
    ///     Execute a command with a handler function
    /// </summary>
    public Task<ResultBox<ExecutionResult>> ExecuteAsync<TCommand>(
        TCommand command,
        Func<TCommand, ICommandContext, Task<ResultBox<EventOrNone>>> handlerFunc,
        CancellationToken cancellationToken = default) where TCommand : ICommand =>
        ResultBoxBridge.CaptureAsync(_core.ExecuteAsync(command, CoreHandler(handlerFunc), cancellationToken));

    /// <summary>
    ///     Execute a handler function without an explicit command
    /// </summary>
    public Task<ResultBox<ExecutionResult>> ExecuteCommandAsync(
        Func<ICommandContext, Task<ResultBox<EventOrNone>>> handlerFunc,
        CancellationToken cancellationToken = default)
    {
        Func<AnonymousCommand, ICoreCommandContext, Task<EventOrNone>> coreHandler = (_, coreCtx) =>
            ResultBoxBridge.UnwrapAsync(handlerFunc(new CommandContextAdapter(coreCtx)));

        return ResultBoxBridge.CaptureAsync(_core.ExecuteAsync(NoCommandInstance, coreHandler, cancellationToken));
    }

    // The user's handler returns a box; the exception-based core receives its value, or the exception a failed box
    // carries (same instance), which the core reports back here as a failed box.
    private static Func<TCommand, ICoreCommandContext, Task<EventOrNone>> CoreHandler<TCommand>()
        where TCommand : ICommandWithHandler<TCommand> =>
        (cmd, coreCtx) => ResultBoxBridge.UnwrapAsync(TCommand.HandleAsync(cmd, new CommandContextAdapter(coreCtx)));

    private static Func<TCommand, ICoreCommandContext, Task<EventOrNone>> CoreHandler<TCommand>(
        Func<TCommand, ICommandContext, Task<ResultBox<EventOrNone>>> handlerFunc) where TCommand : ICommand =>
        (cmd, coreCtx) => ResultBoxBridge.UnwrapAsync(handlerFunc(cmd, new CommandContextAdapter(coreCtx)));

    /// <summary>
    ///     Get the current state for a specific tag state
    /// </summary>
    public Task<ResultBox<TagState>> GetTagStateAsync(TagStateId tagStateId) =>
        ResultBoxBridge.CaptureAsync(_core.GetTagStateAsync(tagStateId));

    public Task<ResultBox<TagState>> GetTagStateAsync(TagStateId tagStateId, CancellationToken cancellationToken) =>
        ResultBoxBridge.CaptureAsync(_core.GetTagStateAsync(tagStateId, cancellationToken));

    /// <summary>
    ///     Execute a single-result query
    /// </summary>
    public Task<ResultBox<TResult>> QueryAsync<TResult>(IQueryCommon<TResult> queryCommon) where TResult : notnull =>
        ResultBoxBridge.CaptureAsync(_core.QueryAsync(queryCommon));

    /// <summary>
    ///     Execute a list query with pagination support
    /// </summary>
    public Task<ResultBox<ListQueryResult<TResult>>> QueryAsync<TResult>(IListQueryCommon<TResult> queryCommon)
        where TResult : notnull =>
        ResultBoxBridge.CaptureAsync(_core.QueryAsync(queryCommon));

    public Task<ResultBox<string>> GetLatestSortableUniqueIdAsync() =>
        ResultBoxBridge.CaptureAsync(_core.GetLatestSortableUniqueIdAsync());

    public Task<ResultBox<ProjectionHeadStatus>> GetProjectionHeadStatusAsync(
        string projectorName,
        string? expectedProjectorVersion = null) =>
        ResultBoxBridge.CaptureAsync(_core.GetProjectionHeadStatusAsync(projectorName, expectedProjectorVersion));

    public Task<ResultBox<EventStoreHeadStatus>> GetEventStoreHeadStatusAsync(bool includeTotalEventCount = false) =>
        ResultBoxBridge.CaptureAsync(_core.GetEventStoreHeadStatusAsync(includeTotalEventCount));

    public Task<ResultBox<SerializableTagState>> GetSerializableTagStateAsync(TagStateId tagStateId) =>
        ResultBoxBridge.CaptureAsync(_core.GetSerializableTagStateAsync(tagStateId));

    public Task<ResultBox<SerializedCommitResult>> CommitSerializableEventsAsync(
        SerializedCommitRequest request,
        CancellationToken cancellationToken = default) =>
        ResultBoxBridge.CaptureAsync(_core.CommitSerializableEventsAsync(request, cancellationToken));

    /// <summary>Opt-in V2 serialized commit with PostgreSQL's durable expected-tag-position protocol.</summary>
    public Task<ResultBox<SerializedCommitResult>> CommitSerializableEventsWithExpectedTagPositionsAsync(
        VersionedExpectedTagPositionSerializedCommitRequest request,
        CancellationToken cancellationToken = default) =>
        ResultBoxBridge.CaptureAsync(_core.CommitSerializableEventsWithExpectedTagPositionsAsync(request, cancellationToken));

    /// <summary>Opt-in: execute a self-handling command with conditional (unique-key) append options.</summary>
    public Task<ResultBox<ExecutionResult>> ExecuteAsync<TCommand>(
        TCommand command,
        CommandExecutionOptions options,
        CancellationToken cancellationToken = default) where TCommand : ICommandWithHandler<TCommand> =>
        ResultBoxBridge.CaptureAsync(_core.ExecuteAsync(command, CoreHandler<TCommand>(), options, cancellationToken));

    /// <summary>Opt-in: execute a command with a handler function and conditional (unique-key) append options.</summary>
    public Task<ResultBox<ExecutionResult>> ExecuteAsync<TCommand>(
        TCommand command,
        Func<TCommand, ICommandContext, Task<ResultBox<EventOrNone>>> handlerFunc,
        CommandExecutionOptions options,
        CancellationToken cancellationToken = default) where TCommand : ICommand =>
        ResultBoxBridge.CaptureAsync(_core.ExecuteAsync(command, CoreHandler(handlerFunc), options, cancellationToken));

    /// <summary>Opt-in WASM boundary: conditional (unique-key) single-event serialized commit.</summary>
    public Task<ResultBox<SerializedConditionalCommitResult>> CommitSerializableEventConditionallyAsync(
        SerializedConditionalCommitRequest request,
        CancellationToken cancellationToken = default) =>
        ResultBoxBridge.CaptureAsync(_core.CommitSerializableEventConditionallyAsync(request, cancellationToken));

    // The serialized boundaries are exception-based core contracts; the boxed methods above are this facade's own API.
    Task<SerializableTagState> ISerializedSekibanDcbExecutor.GetSerializableTagStateAsync(TagStateId tagStateId) =>
        _core.GetSerializableTagStateAsync(tagStateId);

    Task<SerializedCommitResult> ISerializedSekibanDcbExecutor.CommitSerializableEventsAsync(
        SerializedCommitRequest request,
        CancellationToken cancellationToken) =>
        _core.CommitSerializableEventsAsync(request, cancellationToken);

    Task<SerializedCommitResult> ISerializedExpectedTagPositionSekibanDcbExecutor.CommitSerializableEventsWithExpectedTagPositionsAsync(
        VersionedExpectedTagPositionSerializedCommitRequest request,
        CancellationToken cancellationToken) =>
        _core.CommitSerializableEventsWithExpectedTagPositionsAsync(request, cancellationToken);

    Task<SerializedConditionalCommitResult> ISerializedConditionalSekibanDcbExecutor.CommitSerializableEventConditionallyAsync(
        SerializedConditionalCommitRequest request,
        CancellationToken cancellationToken) =>
        _core.CommitSerializableEventConditionallyAsync(request, cancellationToken);

    private sealed record AnonymousCommand : ICommand;
}
