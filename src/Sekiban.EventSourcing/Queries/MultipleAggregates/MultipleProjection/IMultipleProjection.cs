namespace Sekiban.EventSourcing.Queries.MultipleAggregates.MultipleProjection
{
    public interface IMultipleProjection
    {
        Task<Q> GetMultipleProjectionAsync<P, Q>() where P : IMultipleAggregateProjector<Q>, new() where Q : IMultipleAggregateProjectionDto, new();
    }
}
