namespace Sekiban.EventSourcing.Queries.SingleAggregates;

public interface ISingleAggregateProjection
{
    void ApplyEvent(AggregateEvent ev);
}
