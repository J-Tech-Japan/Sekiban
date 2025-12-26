using ResultBoxes;
using Sekiban.Dcb.Actors;
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

        var result = await _core.ExecuteAsync(command, coreHandler, cancellationToken);
        return result.UnwrapBox();
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

        var result = await _core.ExecuteAsync(command, coreHandler, cancellationToken);
        return result.UnwrapBox();
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

        var result = await _core.ExecuteAsync(NoCommandInstance, coreHandler, cancellationToken);
        return result.UnwrapBox();
    }

    /// <summary>
    ///     Get the current state for a specific tag state
    /// </summary>
    public async Task<TagState> GetTagStateAsync(TagStateId tagStateId)
    {
        var result = await _core.GetTagStateAsync(tagStateId);
        return result.UnwrapBox();
    }

    /// <summary>
    ///     Execute a single-result query
    /// </summary>
    public async Task<TResult> QueryAsync<TResult>(IQueryCommon<TResult> queryCommon) where TResult : notnull
    {
        var result = await _core.QueryAsync(queryCommon);
        return result.UnwrapBox();
    }

    /// <summary>
    ///     Execute a list query with pagination support
    /// </summary>
    public async Task<ListQueryResult<TResult>> QueryAsync<TResult>(IListQueryCommon<TResult> queryCommon)
        where TResult : notnull
    {
        var result = await _core.QueryAsync(queryCommon);
        return result.UnwrapBox();
    }

    private sealed record AnonymousCommand : ICommand;
}
