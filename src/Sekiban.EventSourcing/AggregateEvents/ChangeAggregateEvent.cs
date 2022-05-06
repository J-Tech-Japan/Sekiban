namespace Sekiban.EventSourcing.AggregateEvents;

public record ChangeAggregateEvent<T>(Guid AggregateId) : AggregateEvent(AggregateId, typeof(T)) where T : IAggregate;
