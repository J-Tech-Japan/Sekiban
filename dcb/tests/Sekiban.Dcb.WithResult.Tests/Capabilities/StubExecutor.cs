using ResultBoxes;
using Sekiban.Dcb.Commands;
using Sekiban.Dcb.Events;
using Sekiban.Dcb.MultiProjections;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.Tags;
namespace Sekiban.Dcb.WithResult.Tests.Capabilities;

/// <summary>
///     An executor that executes nothing. The guard describes what it resolved; it never calls it — and this stub is
///     how that claim is kept honest: if the guard ever did call one of these, the test would blow up rather than
///     quietly pass.
/// </summary>
public class StubExecutor : ISekibanExecutor
{
    public Task<ResultBox<ExecutionResult>> ExecuteAsync<TCommand>(
        TCommand command,
        Func<TCommand, ICommandContext, Task<ResultBox<EventOrNone>>> handlerFunc,
        CancellationToken cancellationToken = default) where TCommand : ICommand => throw new NotSupportedException();

    public Task<ResultBox<ExecutionResult>> ExecuteCommandAsync(
        Func<ICommandContext, Task<ResultBox<EventOrNone>>> handlerFunc,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<ResultBox<ExecutionResult>> ExecuteAsync<TCommand>(
        TCommand command,
        CancellationToken cancellationToken = default) where TCommand : ICommandWithHandler<TCommand> =>
        throw new NotSupportedException();

    public Task<ResultBox<TagState>> GetTagStateAsync(TagStateId tagStateId) => throw new NotSupportedException();

    public Task<ResultBox<TResult>> QueryAsync<TResult>(IQueryCommon<TResult> queryCommon) where TResult : notnull =>
        throw new NotSupportedException();

    public Task<ResultBox<ListQueryResult<TResult>>> QueryAsync<TResult>(IListQueryCommon<TResult> queryCommon)
        where TResult : notnull => throw new NotSupportedException();

    public Task<ResultBox<string>> GetLatestSortableUniqueIdAsync() => throw new NotSupportedException();

    public Task<ResultBox<ProjectionHeadStatus>> GetProjectionHeadStatusAsync(
        string projectorName,
        string? expectedProjectorVersion = null) => throw new NotSupportedException();

    public Task<ResultBox<EventStoreHeadStatus>> GetEventStoreHeadStatusAsync(bool includeTotalEventCount = false) =>
        throw new NotSupportedException();
}
