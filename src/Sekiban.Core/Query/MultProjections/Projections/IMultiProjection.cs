namespace Sekiban.Core.Query.MultProjections.Projections;

public interface IMultiProjection
{
    Task<MultiProjectionState<TProjectionPayload>> GetMultiProjectionAsync<TProjection, TProjectionPayload>()
        where TProjection : IMultiProjector<TProjectionPayload>, new()
        where TProjectionPayload : IMultiProjectionPayload, new();
}
