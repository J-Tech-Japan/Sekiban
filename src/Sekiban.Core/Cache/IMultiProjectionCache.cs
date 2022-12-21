using Sekiban.Core.Query.MultiProjections;
using Sekiban.Core.Query.MultiProjections.Projections;
namespace Sekiban.Core.Cache;

/// <summary>
///     defines a interface for multi projection Cache
///     Default implementation is <see cref="MultiProjectionCache" />
///     Application developer can implement this interface to provide custom cache implementation
/// </summary>
public interface IMultiProjectionCache
{
    public void Set<TProjection, TProjectionPayload>(
        MultipleMemoryProjectionContainer<TProjection, TProjectionPayload> container)
        where TProjection : IMultiProjector<TProjectionPayload>, new()
        where TProjectionPayload : IMultiProjectionPayloadCommon, new();

    public MultipleMemoryProjectionContainer<TProjection, TProjectionPayload>? Get<TProjection, TProjectionPayload>()
        where TProjection : IMultiProjector<TProjectionPayload>, new()
        where TProjectionPayload : IMultiProjectionPayloadCommon, new();
}
