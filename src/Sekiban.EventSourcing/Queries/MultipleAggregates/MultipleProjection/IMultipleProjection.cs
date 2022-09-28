namespace Sekiban.EventSourcing.Queries.MultipleAggregates.MultipleProjection;

public interface IMultipleProjection
{
    Task<MultipleAggregateProjectionContentsDto<TProjectionContents>> GetMultipleProjectionAsync<TProjection, TProjectionContents>()
        where TProjection : IMultipleAggregateProjector<TProjectionContents>, new()
        where TProjectionContents : IMultipleAggregateProjectionContents, new();
}
