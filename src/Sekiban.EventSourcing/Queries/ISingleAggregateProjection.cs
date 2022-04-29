using Sekiban.EventSourcing.AggregateEvents;
namespace Sekiban.EventSourcing.Queries;

public interface ISingleAggregateProjection
{
    void ApplyEvent(AggregateEvent ev);
}
