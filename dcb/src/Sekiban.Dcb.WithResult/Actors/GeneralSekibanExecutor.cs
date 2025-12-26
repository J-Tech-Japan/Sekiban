using ResultBoxes;
using Sekiban.Dcb.Actors;
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
public class GeneralSekibanExecutor : ISekibanExecutor
{
    private readonly CoreGeneralSekibanExecutor _core;
    private static readonly AnonymousCommand NoCommandInstance = new();

    public GeneralSekibanExecutor(
        IEventStore eventStore,
        IActorObjectAccessor actorAccessor,
        DcbDomainTypes domainTypes,
        IEventPublisher? eventPublisher = null)
    {
        _core = new CoreGeneralSekibanExecutor(eventStore, actorAccessor, domainTypes, eventPublisher);
    }

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

    private sealed record AnonymousCommand : ICommand;
}
