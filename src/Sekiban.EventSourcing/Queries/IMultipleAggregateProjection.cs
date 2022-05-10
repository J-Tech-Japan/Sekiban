namespace Sekiban.EventSourcing.Queries;

public interface IMultipleAggregateProjection : IProjection
{
    void ApplyEvent(AggregateEvent ev);
}
