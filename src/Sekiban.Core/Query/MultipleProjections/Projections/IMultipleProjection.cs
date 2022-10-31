namespace Sekiban.Core.Query.MultipleProjections.Projections;

public interface IMultipleProjection
{
    Task<MultiProjectionState<TProjectionPayload>> GetMultipleProjectionAsync<TProjection, TProjectionPayload>()
        where TProjection : IMultiProjector<TProjectionPayload>, new()
        where TProjectionPayload : IMultiProjectionPayload, new();
}
