namespace Sekiban.EventSourcing.AggregateEvents;

public record CreateAggregateEvent<T>(Guid AggregateId) : AggregateEvent(
    AggregateId,
    typeof(T),
    true)
    where T : IAggregate;
