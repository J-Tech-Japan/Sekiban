using ResultBoxes;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb.Orleans;

/// <summary>
///     Wrapper over <see cref="ISekibanExecutor"/> that unwraps ResultBox values and surfaces exceptions instead.
///     Delegates all work to an underlying <see cref="OrleansDcbExecutor"/> (or any <see cref="ISekibanExecutor"/> implementation).
/// </summary>
public class OrleansDcbExecutorWithoutResult : ISekibanExecutorWithoutResult
{
    private readonly ISekibanExecutor _executor;

    public OrleansDcbExecutorWithoutResult(OrleansDcbExecutor executor)
        : this((ISekibanExecutor)executor) { }

    public OrleansDcbExecutorWithoutResult(ISekibanExecutor executor)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
    }

    public Task<ExecutionResult> ExecuteAsync<TCommand>(
        TCommand command,
        Func<TCommand, ICommandContext, Task<EventOrNone>> handlerFunc,
        CancellationToken cancellationToken = default) where TCommand : ICommand =>
        _executor.ExecuteAsync(command, WrapHandler(handlerFunc), cancellationToken).UnwrapBox();

    public Task<ExecutionResult> ExecuteAsync<TCommand>(
        TCommand command,
        CancellationToken cancellationToken = default) where TCommand : ICommandWithHandlerWithoutResult<TCommand> =>
        _executor.ExecuteAsync(
            command,
            WrapHandler<TCommand>(static (cmd, ctx) => TCommand.HandleAsync(cmd, ctx)),
            cancellationToken).UnwrapBox();

    public Task<TagState> GetTagStateAsync(TagStateId tagStateId) =>
        _executor.GetTagStateAsync(tagStateId).UnwrapBox();

    public Task<TResult> QueryAsync<TResult>(IQueryCommon<TResult> queryCommon) where TResult : notnull =>
        _executor.QueryAsync(queryCommon).UnwrapBox();

    public Task<ListQueryResult<TResult>> QueryAsync<TResult>(IListQueryCommon<TResult> queryCommon)
        where TResult : notnull =>
        _executor.QueryAsync(queryCommon).UnwrapBox();

    private static Func<TCommand, ICommandContext, Task<ResultBox<EventOrNone>>> WrapHandler<TCommand>(
        Func<TCommand, ICommandContext, Task<EventOrNone>> handlerFunc)
        where TCommand : ICommand
    {
        if (handlerFunc is null)
        {
            throw new ArgumentNullException(nameof(handlerFunc));
        }

        return async (command, context) =>
        {
            try
            {
                var result = await handlerFunc(command, context).ConfigureAwait(false);
                return ResultBox.FromValue(result);
            }
            catch (Exception ex)
            {
                return ResultBox<EventOrNone>.Error(ex);
            }
        };
    }
}
