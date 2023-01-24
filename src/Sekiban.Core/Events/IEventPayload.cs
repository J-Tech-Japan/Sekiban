using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Events;

public interface IEventPayload<TAggregatePayload, TEventPayload> : IEventPayloadApplicableTo<TAggregatePayload>
    where TAggregatePayload : IAggregatePayload where TEventPayload : IEventPayload<TAggregatePayload, TEventPayload>
{
#if NET7_0_OR_GREATER
    public static abstract TAggregatePayload OnEvent(TAggregatePayload payload, Event<TEventPayload> ev);
#else
    public TAggregatePayload OnEventInstance(TAggregatePayload payload, Event<TEventPayload> ev);
#endif
}
