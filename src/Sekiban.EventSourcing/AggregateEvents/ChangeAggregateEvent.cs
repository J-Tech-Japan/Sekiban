namespace Sekiban.EventSourcing.AggregateEvents;

public record ChangeAggregateEvent<T, TEventPayload> : AggregateEvent<TEventPayload> where T : IAggregate where TEventPayload : IEventPayload
{
    public ChangeAggregateEvent(Guid aggregateId, TEventPayload payload) : base(aggregateId, payload, typeof(T)) { }
}
