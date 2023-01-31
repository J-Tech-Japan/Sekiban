using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Events;

public interface
    IEventPayload<TAggregatePayload, TEventPayload> : IEventPayload<TAggregatePayload, TAggregatePayload, TEventPayload>
    where TAggregatePayload : IAggregatePayloadCommon, new()
    where TEventPayload : IEventPayload<TAggregatePayload, TAggregatePayload, TEventPayload>
{
}
public interface
    IEventPayload<TAggregatePayloadIn, TAggregatePayloadOut, TEventPayload> : IEventPayloadApplicableTo<TAggregatePayloadIn>
    where TAggregatePayloadIn : IAggregatePayloadCommon
    where TEventPayload : IEventPayload<TAggregatePayloadIn, TAggregatePayloadOut, TEventPayload>
    where TAggregatePayloadOut : IAggregatePayloadCommon, new()
{
#if NET7_0_OR_GREATER
    public static abstract TAggregatePayloadOut OnEvent(TAggregatePayloadIn aggregatePayload, Event<TEventPayload> ev);
#else
    public TAggregatePayloadOut OnEventInstance(TAggregatePayloadIn aggregatePayload, Event<TEventPayload> ev);
#endif
}
