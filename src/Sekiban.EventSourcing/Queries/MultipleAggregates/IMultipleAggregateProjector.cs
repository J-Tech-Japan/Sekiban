namespace Sekiban.EventSourcing.Queries.MultipleAggregates;

public interface IMultipleAggregateProjector<TDto> : IProjection where TDto : IMultipleAggregateProjectionDto
{
    void ApplyEvent(AggregateEvent ev);
    TDto ToDto();
    void ApplySnapshot(TDto snapshot);
}
