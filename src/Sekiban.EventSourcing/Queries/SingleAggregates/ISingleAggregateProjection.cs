namespace Sekiban.EventSourcing.Queries.SingleAggregates;

public interface ISingleAggregateProjection
{
    void ApplyEvent(IAggregateEvent ev);
    public bool CanApplyEvent(IAggregateEvent ev);
}
