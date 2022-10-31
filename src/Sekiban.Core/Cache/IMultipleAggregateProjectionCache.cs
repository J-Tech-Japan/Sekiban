using Sekiban.Core.Query.MultipleProjections;
using Sekiban.Core.Query.MultipleProjections.Projections;
namespace Sekiban.Core.Cache;

public interface IMultipleAggregateProjectionCache
{
    public void Set<TProjection, TProjectionPayload>(MultipleMemoryProjectionContainer<TProjection, TProjectionPayload> container)
        where TProjection : IMultiProjector<TProjectionPayload>, new()
        where TProjectionPayload : IMultiProjectionPayload, new();
    public MultipleMemoryProjectionContainer<TProjection, TProjectionPayload> Get<TProjection, TProjectionPayload>()
        where TProjection : IMultiProjector<TProjectionPayload>, new()
        where TProjectionPayload : IMultiProjectionPayload, new();
}
