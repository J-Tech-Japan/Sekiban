using Sekiban.Core.Query.SingleProjections;
using Sekiban.Core.Query.SingleProjections.Projections;
using ISingleProjection = Sekiban.Core.Query.SingleProjections.ISingleProjection;
namespace Sekiban.Core.Cache;

public interface ISingleProjectionCache
{
    public void SetContainer<TAggregate, TState>(
        Guid aggregateId,
        SingleMemoryCacheProjectionContainer<TAggregate, TState> container) where TAggregate : IAggregateIdentifier, ISingleProjection
        where TState : IAggregateIdentifier;
    public SingleMemoryCacheProjectionContainer<TAggregate, TState>? GetContainer<TAggregate, TState>(Guid aggregateId)
        where TAggregate : IAggregateIdentifier, ISingleProjection where TState : IAggregateIdentifier;
}
