using Sekiban.Core.Query.SingleProjections;
using Sekiban.Core.Query.SingleProjections.Projections;
using ISingleProjection = Sekiban.Core.Query.SingleProjections.ISingleProjection;
namespace Sekiban.Core.Cache;

public interface ISingleProjectionCache
{
    public void SetContainer<TAggregate, TState>(
        Guid aggregateId,
        SingleMemoryCacheProjectionContainer<TAggregate, TState> container) where TAggregate : IAggregateCommon, ISingleProjection
        where TState : IAggregateCommon;
    public SingleMemoryCacheProjectionContainer<TAggregate, TState>? GetContainer<TAggregate, TState>(Guid aggregateId)
        where TAggregate : IAggregateCommon, ISingleProjection where TState : IAggregateCommon;
}
