namespace Sekiban.EventSourcing.AggregateEvents;

public interface ICreatedAggregateEventPayload<TAggregate> : ICreatedEventPayload, IAggregatePointerEvent<TAggregate> where TAggregate : IAggregate
{
}
