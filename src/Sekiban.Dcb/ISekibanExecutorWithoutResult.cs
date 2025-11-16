using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Tags;

namespace Sekiban.Dcb;

/// <summary>
///     Executor interface that exposes operations without ResultBox wrappers.
///     Internally relies on <see cref="ISekibanExecutor"/> and surfaces exception-based flow.
/// </summary>
public interface ISekibanExecutorWithoutResult
{
    Task<ExecutionResult> ExecuteAsync<TCommand>(
        TCommand command,
        Func<TCommand, ICommandContextWithoutResult, Task<EventOrNone>> handlerFunc,
        CancellationToken cancellationToken = default) where TCommand : ICommand;

    Task<ExecutionResult> ExecuteAsync<TCommand>(
        TCommand command,
        CancellationToken cancellationToken = default) where TCommand : ICommandWithHandlerWithoutResult<TCommand>;

    Task<TagState> GetTagStateAsync(TagStateId tagStateId);
    Task<TagState> GetTagStateAsync<TProjector>(ITag tag) where TProjector : ITagProjector<TProjector> =>
        GetTagStateAsync(TagStateId.FromProjector<TProjector>(tag));

    Task<TResult> QueryAsync<TResult>(IQueryCommon<TResult> queryCommon) where TResult : notnull;

    Task<ListQueryResult<TResult>> QueryAsync<TResult>(IListQueryCommon<TResult> queryCommon)
        where TResult : notnull;
}
