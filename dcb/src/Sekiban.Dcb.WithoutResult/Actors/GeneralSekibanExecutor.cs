using ResultBoxes;
using Sekiban.Dcb.Boundaries;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Capabilities;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.Actors;

/// <summary>
///     Exception-based implementation of ISekibanExecutor
///     Wraps CoreGeneralSekibanExecutor and unwraps ResultBox, throwing exceptions on errors
///     This implementation uses exceptions for all error handling
/// </summary>
public class GeneralSekibanExecutor : GeneralSekibanExecutorBase, ISekibanExecutor, ISerializedSekibanDcbExecutor,
    IExecutorRuntimeDescriptorProvider,
    IConditionalCommandExecutor, ISerializedConditionalSekibanDcbExecutor
{
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
        : base(eventStore, actorAccessor, domainTypes, eventPublisher, executedUserProvider)
    {
    }

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
