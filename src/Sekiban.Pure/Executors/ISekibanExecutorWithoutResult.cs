using Sekiban.Pure.Aggregates;
using Sekiban.Pure.Command.Executor;
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
using Sekiban.Pure.Projectors;
using Sekiban.Pure.Query;

namespace Sekiban.Pure.Executors;

/// <summary>
///     Executor variant that exposes command and query operations without ResultBox wrappers.
///     Consumers can rely on exception flow while reusing existing executors internally.
/// </summary>
public interface ISekibanExecutorWithoutResult
{
    SekibanDomainTypes GetDomainTypes();
    Task<CommandResponse> CommandAsync(ICommandWithHandlerSerializable command, IEvent? relatedEvent = null);
    Task<TResult> QueryAsync<TResult>(IQueryCommon<TResult> queryCommon) where TResult : notnull;
    Task<ListQueryResult<TResult>> QueryAsync<TResult>(IListQueryCommon<TResult> queryCommon)
        where TResult : notnull;
    Task<Aggregate> LoadAggregateAsync<TAggregateProjector>(PartitionKeys partitionKeys)
        where TAggregateProjector : IAggregateProjector, new();
}
