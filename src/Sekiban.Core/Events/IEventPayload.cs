using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Events;

public interface IEventPayload<TAggregatePayload, TEventPayload> : IEventPayload<TAggregatePayload, TAggregatePayload, TEventPayload>
    where TAggregatePayload : IAggregatePayloadCommon where TEventPayload : IEventPayload<TAggregatePayload, TAggregatePayload, TEventPayload>
{
}
public interface IEventPayload<TAggregatePayloadIn, TAggregatePayloadOut, TEventPayload> : IEventPayloadApplicableTo<TAggregatePayloadIn>
    where TAggregatePayloadIn : IAggregatePayloadCommon
    where TEventPayload : IEventPayload<TAggregatePayloadIn, TAggregatePayloadOut, TEventPayload>
    where TAggregatePayloadOut : IAggregatePayloadCommon
{
    public static abstract TAggregatePayloadOut OnEvent(TAggregatePayloadIn aggregatePayload, Event<TEventPayload> ev);
}
