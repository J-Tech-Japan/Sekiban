namespace Sekiban.EventSourcing.AggregateEvents;

public record CreateAggregateEvent<T, TEventPayload> : AggregateEvent<TEventPayload> where T : IAggregate where TEventPayload : IEventPayload
{
    public CreateAggregateEvent(Guid aggregateId, TEventPayload payload) : base(aggregateId, payload, typeof(T), true) { }
}
