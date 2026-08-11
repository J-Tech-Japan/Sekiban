using ResultBoxes;
using Sekiban.Dcb.Boundaries;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Capabilities;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Common;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.ServiceId;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.Actors;

/// <summary>
///     Exception-based implementation of ISekibanExecutor
///     Wraps CoreGeneralSekibanExecutor and unwraps ResultBox, throwing exceptions on errors
///     This implementation uses exceptions for all error handling
/// </summary>
public class GeneralSekibanExecutor : ISekibanExecutor, ISerializedSekibanDcbExecutor, IExecutorRuntimeDescriptorProvider,
    IConditionalCommandExecutor, ISerializedConditionalSekibanDcbExecutor
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
    {
        _actorAccessor = actorAccessor;
        _core = new CoreGeneralSekibanExecutor(
            eventStore,
            actorAccessor,
            domainTypes,
            eventPublisher,
            executedUserProvider,
            sortableUniqueIdGenerator,
            sortableUniqueIdSeedCoordinator,
            serviceIdProvider);
    }

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
    public async Task<ExecutionResult> ExecuteAsync<TCommand>(
        TCommand command,
        CancellationToken cancellationToken = default) where TCommand : ICommandWithHandler<TCommand>
    {
        // Create a wrapper that converts ICommandContext to ICoreCommandContext and handles exceptions
        Func<TCommand, ICoreCommandContext, Task<ResultBox<EventOrNone>>> coreHandler = async (cmd, coreCtx) =>
        {
            try
            {
                var contextAdapter = new CommandContextAdapter(coreCtx);
                var result = await TCommand.HandleAsync(cmd, contextAdapter);
                return ResultBox.FromValue(result);
            }
            catch (Exception ex)
            {
                return ResultBox<EventOrNone>.Error(ex);
            }
        };

        return await GuardedUnwrap.UnwrapAsync(
            _core.ExecuteAsync(command, coreHandler, cancellationToken),
            new BoundaryContext("ISekibanExecutor.ExecuteAsync", typeof(TCommand).Name));
    }

    /// <summary>
    ///     Execute a command with a handler function
    /// </summary>
    public async Task<ExecutionResult> ExecuteAsync<TCommand>(
        TCommand command,
        Func<TCommand, ICommandContext, Task<EventOrNone>> handlerFunc,
        CancellationToken cancellationToken = default) where TCommand : ICommand
    {
        // Wrap the handler to convert ICommandContext to ICoreCommandContext and handle exceptions
        Func<TCommand, ICoreCommandContext, Task<ResultBox<EventOrNone>>> coreHandler = async (cmd, coreCtx) =>
        {
            try
            {
                var contextAdapter = new CommandContextAdapter(coreCtx);
                var result = await handlerFunc(cmd, contextAdapter);
                return ResultBox.FromValue(result);
            }
            catch (Exception ex)
            {
                return ResultBox<EventOrNone>.Error(ex);
            }
        };

        return await GuardedUnwrap.UnwrapAsync(
            _core.ExecuteAsync(command, coreHandler, cancellationToken),
            new BoundaryContext("ISekibanExecutor.ExecuteAsync", typeof(TCommand).Name));
    }

    /// <summary>
    ///     Execute a handler function without an explicit command
    /// </summary>
    public async Task<ExecutionResult> ExecuteCommandAsync(
        Func<ICommandContext, Task<EventOrNone>> handlerFunc,
        CancellationToken cancellationToken = default)
    {
        Func<AnonymousCommand, ICoreCommandContext, Task<ResultBox<EventOrNone>>> coreHandler = async (_, coreCtx) =>
        {
            try
            {
                var contextAdapter = new CommandContextAdapter(coreCtx);
                var result = await handlerFunc(contextAdapter);
                return ResultBox.FromValue(result);
            }
            catch (Exception ex)
            {
                return ResultBox<EventOrNone>.Error(ex);
            }
        };

        return await GuardedUnwrap.UnwrapAsync(
            _core.ExecuteAsync(NoCommandInstance, coreHandler, cancellationToken),
            new BoundaryContext("ISekibanExecutor.ExecuteCommandAsync"));
    }

    /// <summary>
    ///     Get the current state for a specific tag state
    /// </summary>
    public async Task<TagState> GetTagStateAsync(TagStateId tagStateId)
    {
        return await GuardedUnwrap.UnwrapAsync(
            _core.GetTagStateAsync(tagStateId),
            new BoundaryContext("ISekibanExecutor.GetTagStateAsync", tagStateId.GetTagStateId()));
    }

    /// <summary>
    ///     Execute a single-result query
    /// </summary>
    public async Task<TResult> QueryAsync<TResult>(IQueryCommon<TResult> queryCommon) where TResult : notnull
    {
        return await GuardedUnwrap.UnwrapAsync(
            _core.QueryAsync(queryCommon),
            new BoundaryContext("ISekibanExecutor.QueryAsync", queryCommon?.GetType().Name));
    }

    /// <summary>
    ///     Execute a list query with pagination support
    /// </summary>
    public async Task<ListQueryResult<TResult>> QueryAsync<TResult>(IListQueryCommon<TResult> queryCommon)
        where TResult : notnull
    {
        return await GuardedUnwrap.UnwrapAsync(
            _core.QueryAsync(queryCommon),
            new BoundaryContext("ISekibanExecutor.QueryAsync (list)", queryCommon?.GetType().Name));
    }

    public async Task<string> GetLatestSortableUniqueIdAsync()
    {
        return await GuardedUnwrap.UnwrapAsync(_core.GetLatestSortableUniqueIdAsync(), new BoundaryContext("ISekibanExecutor.GetLatestSortableUniqueIdAsync"));
    }

    public async Task<ProjectionHeadStatus> GetProjectionHeadStatusAsync(
        string projectorName,
        string? expectedProjectorVersion = null)
    {
        return await GuardedUnwrap.UnwrapAsync(
            _core.GetProjectionHeadStatusAsync(projectorName, expectedProjectorVersion),
            new BoundaryContext("ISekibanExecutor.GetProjectionHeadStatusAsync", projectorName));
    }

    public async Task<EventStoreHeadStatus> GetEventStoreHeadStatusAsync(bool includeTotalEventCount = false)
    {
        return await GuardedUnwrap.UnwrapAsync(_core.GetEventStoreHeadStatusAsync(includeTotalEventCount), new BoundaryContext("ISekibanExecutor.GetEventStoreHeadStatusAsync"));
    }

    public Task<ResultBox<SerializableTagState>> GetSerializableTagStateAsync(TagStateId tagStateId) =>
        _core.GetSerializableTagStateAsync(tagStateId);

    public Task<ResultBox<SerializedCommitResult>> CommitSerializableEventsAsync(
        SerializedCommitRequest request,
        CancellationToken cancellationToken = default) =>
        _core.CommitSerializableEventsAsync(request, cancellationToken);

    /// <summary>Opt-in: execute a self-handling command with conditional (unique-key) append options.</summary>
    public async Task<ExecutionResult> ExecuteAsync<TCommand>(
        TCommand command,
        CommandExecutionOptions options,
        CancellationToken cancellationToken = default) where TCommand : ICommandWithHandler<TCommand>
    {
        Func<TCommand, ICoreCommandContext, Task<ResultBox<EventOrNone>>> coreHandler = async (cmd, coreCtx) =>
        {
            try
            {
                var contextAdapter = new CommandContextAdapter(coreCtx);
                var result = await TCommand.HandleAsync(cmd, contextAdapter);
                return ResultBox.FromValue(result);
            }
            catch (Exception ex)
            {
                return ResultBox<EventOrNone>.Error(ex);
            }
        };
        return await GuardedUnwrap.UnwrapAsync(
            _core.ExecuteAsync(command, coreHandler, options, cancellationToken),
            new BoundaryContext("ISekibanExecutor.ExecuteAsync", typeof(TCommand).Name));
    }

    /// <summary>Opt-in: execute a command with a handler function and conditional (unique-key) append options.</summary>
    public async Task<ExecutionResult> ExecuteAsync<TCommand>(
        TCommand command,
        Func<TCommand, ICommandContext, Task<EventOrNone>> handlerFunc,
        CommandExecutionOptions options,
        CancellationToken cancellationToken = default) where TCommand : ICommand
    {
        Func<TCommand, ICoreCommandContext, Task<ResultBox<EventOrNone>>> coreHandler = async (cmd, coreCtx) =>
        {
            try
            {
                var contextAdapter = new CommandContextAdapter(coreCtx);
                var result = await handlerFunc(cmd, contextAdapter);
                return ResultBox.FromValue(result);
            }
            catch (Exception ex)
            {
                return ResultBox<EventOrNone>.Error(ex);
            }
        };
        return await GuardedUnwrap.UnwrapAsync(
            _core.ExecuteAsync(command, coreHandler, options, cancellationToken),
            new BoundaryContext("ISekibanExecutor.ExecuteAsync", typeof(TCommand).Name));
    }

    /// <summary>Opt-in WASM boundary: conditional (unique-key) single-event serialized commit.</summary>
    public Task<ResultBox<SerializedConditionalCommitResult>> CommitSerializableEventConditionallyAsync(
        SerializedConditionalCommitRequest request,
        CancellationToken cancellationToken = default) =>
        _core.CommitSerializableEventConditionallyAsync(request, cancellationToken);

    private sealed record AnonymousCommand : ICommand;
}
