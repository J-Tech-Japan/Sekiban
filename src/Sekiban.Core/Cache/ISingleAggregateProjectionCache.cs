using Sekiban.Core.Query.SingleAggregate;
using Sekiban.Core.Query.SingleAggregate.SingleProjection;
namespace Sekiban.Core.Cache;

public interface ISingleAggregateProjectionCache
{
    public void SetContainer<TAggregate, TState>(
        Guid aggregateId,
        SingleMemoryCacheProjectionContainer<TAggregate, TState> container) where TAggregate : ISingleAggregate, ISingleAggregateProjection
        where TState : ISingleAggregate;
    public SingleMemoryCacheProjectionContainer<TAggregate, TState>? GetContainer<TAggregate, TState>(Guid aggregateId)
        where TAggregate : ISingleAggregate, ISingleAggregateProjection where TState : ISingleAggregate;
}
