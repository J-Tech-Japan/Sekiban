using ResultBoxes;
using Sekiban.Dcb.Actors;
using Sekiban.Dcb.Capabilities;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Storage;
using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.Actors;

/// <summary>
///     ResultBox-based implementation of ISekibanExecutor
///     Wraps CoreGeneralSekibanExecutor to provide the public API
///     This implementation uses ResultBox for all error handling
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
    public async Task<ResultBox<ExecutionResult>> ExecuteAsync<TCommand>(
        TCommand command,
        CancellationToken cancellationToken = default) where TCommand : ICommandWithHandler<TCommand>
    {
        // Create a wrapper that converts ICommandContext to ICoreCommandContext
        Func<TCommand, ICoreCommandContext, Task<ResultBox<EventOrNone>>> coreHandler = async (cmd, coreCtx) =>
        {
            var contextAdapter = new CommandContextAdapter(coreCtx);
            return await TCommand.HandleAsync(cmd, contextAdapter);
        };

        return await _core.ExecuteAsync(command, coreHandler, cancellationToken);
    }

    /// <summary>
    ///     Execute a command with a handler function
    /// </summary>
    public async Task<ResultBox<ExecutionResult>> ExecuteAsync<TCommand>(
        TCommand command,
        Func<TCommand, ICommandContext, Task<ResultBox<EventOrNone>>> handlerFunc,
        CancellationToken cancellationToken = default) where TCommand : ICommand
    {
        // Wrap the handler to convert ICommandContext to ICoreCommandContext
        Func<TCommand, ICoreCommandContext, Task<ResultBox<EventOrNone>>> coreHandler = (cmd, coreCtx) =>
        {
            var contextAdapter = new CommandContextAdapter(coreCtx);
            return handlerFunc(cmd, contextAdapter);
        };

        return await _core.ExecuteAsync(command, coreHandler, cancellationToken);
    }

    /// <summary>
    ///     Execute a handler function without an explicit command
    /// </summary>
    public Task<ResultBox<ExecutionResult>> ExecuteCommandAsync(
        Func<ICommandContext, Task<ResultBox<EventOrNone>>> handlerFunc,
        CancellationToken cancellationToken = default)
    {
        Func<AnonymousCommand, ICoreCommandContext, Task<ResultBox<EventOrNone>>> coreHandler = (_, coreCtx) =>
        {
            var contextAdapter = new CommandContextAdapter(coreCtx);
            return handlerFunc(contextAdapter);
        };

        return _core.ExecuteAsync(NoCommandInstance, coreHandler, cancellationToken);
    }

    /// <summary>
    ///     Get the current state for a specific tag state
    /// </summary>
    public Task<ResultBox<TagState>> GetTagStateAsync(TagStateId tagStateId) =>
        _core.GetTagStateAsync(tagStateId);

    /// <summary>
    ///     Execute a single-result query
    /// </summary>
    public Task<ResultBox<TResult>> QueryAsync<TResult>(IQueryCommon<TResult> queryCommon) where TResult : notnull =>
        _core.QueryAsync(queryCommon);

    /// <summary>
    ///     Execute a list query with pagination support
    /// </summary>
    public Task<ResultBox<ListQueryResult<TResult>>> QueryAsync<TResult>(IListQueryCommon<TResult> queryCommon)
        where TResult : notnull =>
        _core.QueryAsync(queryCommon);

    public Task<ResultBox<string>> GetLatestSortableUniqueIdAsync() =>
        _core.GetLatestSortableUniqueIdAsync();

    public Task<ResultBox<ProjectionHeadStatus>> GetProjectionHeadStatusAsync(
        string projectorName,
        string? expectedProjectorVersion = null) =>
        _core.GetProjectionHeadStatusAsync(projectorName, expectedProjectorVersion);

    public Task<ResultBox<EventStoreHeadStatus>> GetEventStoreHeadStatusAsync(bool includeTotalEventCount = false) =>
        _core.GetEventStoreHeadStatusAsync(includeTotalEventCount);

    public Task<ResultBox<SerializableTagState>> GetSerializableTagStateAsync(TagStateId tagStateId) =>
        _core.GetSerializableTagStateAsync(tagStateId);

    public Task<ResultBox<SerializedCommitResult>> CommitSerializableEventsAsync(
        SerializedCommitRequest request,
        CancellationToken cancellationToken = default) =>
        _core.CommitSerializableEventsAsync(request, cancellationToken);

    /// <summary>Opt-in: execute a self-handling command with conditional (unique-key) append options.</summary>
    public Task<ResultBox<ExecutionResult>> ExecuteAsync<TCommand>(
        TCommand command,
        CommandExecutionOptions options,
        CancellationToken cancellationToken = default) where TCommand : ICommandWithHandler<TCommand>
    {
        Func<TCommand, ICoreCommandContext, Task<ResultBox<EventOrNone>>> coreHandler = async (cmd, coreCtx) =>
        {
            var contextAdapter = new CommandContextAdapter(coreCtx);
            return await TCommand.HandleAsync(cmd, contextAdapter);
        };
        return _core.ExecuteAsync(command, coreHandler, options, cancellationToken);
    }

    /// <summary>Opt-in: execute a command with a handler function and conditional (unique-key) append options.</summary>
    public Task<ResultBox<ExecutionResult>> ExecuteAsync<TCommand>(
        TCommand command,
        Func<TCommand, ICommandContext, Task<ResultBox<EventOrNone>>> handlerFunc,
        CommandExecutionOptions options,
        CancellationToken cancellationToken = default) where TCommand : ICommand
    {
        Func<TCommand, ICoreCommandContext, Task<ResultBox<EventOrNone>>> coreHandler = (cmd, coreCtx) =>
        {
            var contextAdapter = new CommandContextAdapter(coreCtx);
            return handlerFunc(cmd, contextAdapter);
        };
        return _core.ExecuteAsync(command, coreHandler, options, cancellationToken);
    }

    /// <summary>Opt-in WASM boundary: conditional (unique-key) single-event serialized commit.</summary>
    public Task<ResultBox<SerializedConditionalCommitResult>> CommitSerializableEventConditionallyAsync(
        SerializedConditionalCommitRequest request,
        CancellationToken cancellationToken = default) =>
        _core.CommitSerializableEventConditionallyAsync(request, cancellationToken);

    private sealed record AnonymousCommand : ICommand;
}
