namespace Sekiban.Core.Query.MultipleAggregate.MultipleProjection;

public interface IMultipleProjection
{
    Task<MultipleAggregateProjectionState<TProjectionPayload>> GetMultipleProjectionAsync<TProjection, TProjectionPayload>()
        where TProjection : IMultipleAggregateProjector<TProjectionPayload>, new()
        where TProjectionPayload : IMultipleAggregateProjectionPayload, new();
}
