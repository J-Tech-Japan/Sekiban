using Sekiban.Core.Query.MultipleAggregate;
using Sekiban.Core.Query.MultipleAggregate.MultipleProjection;
namespace Sekiban.Core.Cache;

public interface IMultipleAggregateProjectionCache
{
    public void Set<TProjection, TProjectionPayload>(MultipleMemoryProjectionContainer<TProjection, TProjectionPayload> container)
        where TProjection : IMultipleAggregateProjector<TProjectionPayload>, new()
        where TProjectionPayload : IMultipleAggregateProjectionPayload, new();
    public MultipleMemoryProjectionContainer<TProjection, TProjectionPayload> Get<TProjection, TProjectionPayload>()
        where TProjection : IMultipleAggregateProjector<TProjectionPayload>, new()
        where TProjectionPayload : IMultipleAggregateProjectionPayload, new();
}
