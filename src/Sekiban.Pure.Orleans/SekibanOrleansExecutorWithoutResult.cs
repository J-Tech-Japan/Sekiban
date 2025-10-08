using ResultBoxes;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Command.Executor;
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
using Sekiban.Pure.Executors;
using Sekiban.Pure.Projectors;
using Sekiban.Pure.Query;

namespace Sekiban.Pure.Orleans;

/// <summary>
///     Wrapper around <see cref="ISekibanExecutor"/> that unwraps <see cref="ResultBox{T}"/> results.
///     Provides exception-based flow while reusing the ResultBox-oriented implementation.
/// </summary>
public class SekibanOrleansExecutorWithoutResult : ISekibanExecutorWithoutResult
{
    private readonly ISekibanExecutor _executor;

    public SekibanOrleansExecutorWithoutResult(SekibanOrleansExecutor executor)
        : this((ISekibanExecutor)executor) { }

    public SekibanOrleansExecutorWithoutResult(ISekibanExecutor executor)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
    }

    public SekibanDomainTypes GetDomainTypes() => _executor.GetDomainTypes();

    public Task<CommandResponse> CommandAsync(ICommandWithHandlerSerializable command, IEvent? relatedEvent = null) =>
        _executor.CommandAsync(command, relatedEvent).UnwrapBox();

    public Task<TResult> QueryAsync<TResult>(IQueryCommon<TResult> queryCommon) where TResult : notnull =>
        _executor.QueryAsync(queryCommon).UnwrapBox();

    public Task<ListQueryResult<TResult>> QueryAsync<TResult>(IListQueryCommon<TResult> queryCommon)
        where TResult : notnull =>
        _executor.QueryAsync(queryCommon).UnwrapBox();

    public Task<Aggregate> LoadAggregateAsync<TAggregateProjector>(PartitionKeys partitionKeys)
        where TAggregateProjector : IAggregateProjector, new() =>
        _executor.LoadAggregateAsync<TAggregateProjector>(partitionKeys).UnwrapBox();
}
