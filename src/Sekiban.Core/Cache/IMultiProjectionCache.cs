using Sekiban.Core.Query.MultiProjections;
using Sekiban.Core.Query.MultiProjections.Projections;
namespace Sekiban.Core.Cache;

public interface IMultiProjectionCache
{
    public void Set<TProjection, TProjectionPayload>(MultipleMemoryProjectionContainer<TProjection, TProjectionPayload> container)
        where TProjection : IMultiProjector<TProjectionPayload>, new()
        where TProjectionPayload : IMultiProjectionPayload, new();
    public MultipleMemoryProjectionContainer<TProjection, TProjectionPayload>? Get<TProjection, TProjectionPayload>()
        where TProjection : IMultiProjector<TProjectionPayload>, new()
        where TProjectionPayload : IMultiProjectionPayload, new();
}
