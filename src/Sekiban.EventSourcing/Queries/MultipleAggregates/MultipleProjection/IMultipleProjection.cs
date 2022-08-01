namespace Sekiban.EventSourcing.Queries.MultipleAggregates;

public interface IMultipleProjection
{
    Task<Q> GetMultipleProjectionAsync<P, Q>() where P : IMultipleAggregateProjector<Q>, new() where Q : IMultipleAggregateProjectionDto;
}
