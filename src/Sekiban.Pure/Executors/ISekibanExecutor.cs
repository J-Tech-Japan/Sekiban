using ResultBoxes;
using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Command.Executor;
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
using Sekiban.Pure.Projectors;
using Sekiban.Pure.Query;
namespace Sekiban.Pure.Executors;

public interface ISekibanExecutor
{
    public SekibanDomainTypes GetDomainTypes();
    public Task<ResultBox<CommandResponse>> CommandAsync(
        ICommandWithHandlerSerializable command,
        IEvent? relatedEvent = null);
    public Task<ResultBox<TResult>> QueryAsync<TResult>(IQueryCommon<TResult> queryCommon)
        where TResult : notnull;
    public Task<ResultBox<ListQueryResult<TResult>>> QueryAsync<TResult>(IListQueryCommon<TResult> queryCommon)
        where TResult : notnull;
    public Task<ResultBox<Aggregate>> LoadAggregateAsync<TAggregateProjector>(PartitionKeys partitionKeys)
        where TAggregateProjector : IAggregateProjector, new();
}