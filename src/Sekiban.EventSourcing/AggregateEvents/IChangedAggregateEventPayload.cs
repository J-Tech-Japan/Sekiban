namespace Sekiban.EventSourcing.AggregateEvents;

public interface IChangedAggregateEventPayload<TAggregate> : IChangedEventPayload, IAggregatePointerEvent<TAggregate> where TAggregate : IAggregate
{
}
