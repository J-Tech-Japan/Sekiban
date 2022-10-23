using Sekiban.Core.Query.SingleAggregate;
using Sekiban.Core.Query.SingleAggregate.SingleProjection;
namespace Sekiban.Core.Cache;

public interface ISingleAggregateProjectionCache
{
    public void SetContainer<TAggregate, TDto>(
        Guid aggregateId,
        SingleMemoryCacheProjectionContainer<TAggregate, TDto> container) where TAggregate : ISingleAggregate, ISingleAggregateProjection
        where TDto : ISingleAggregate;
    public SingleMemoryCacheProjectionContainer<TAggregate, TDto>? GetContainer<TAggregate, TDto>(Guid aggregateId)
        where TAggregate : ISingleAggregate, ISingleAggregateProjection where TDto : ISingleAggregate;
}
