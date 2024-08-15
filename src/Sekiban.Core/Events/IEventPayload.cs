using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Events;

/// <summary>
///     Event Payload Interface
///     This two generic type is normal (In / Out same Aggregate payload)
/// </summary>
/// <typeparam name="TAggregatePayload"></typeparam>
/// <typeparam name="TEventPayload"> Need to put Event Payload's own type.</typeparam>
public interface
    IEventPayload<TAggregatePayload, TEventPayload> : IEventPayload<TAggregatePayload, TAggregatePayload, TEventPayload>
    where TAggregatePayload : IAggregatePayloadGeneratable<TAggregatePayload>
    where TEventPayload : IEventPayload<TAggregatePayload, TAggregatePayload, TEventPayload>;
/// <summary>
///     Event Payload Interface
///     This three generic type is used when aggregate uses SubTypes and Event Changes Subtype Type (In / Out has Different
///     Aggregate payload)
/// </summary>
/// <typeparam name="TAggregatePayloadIn"></typeparam>
/// <typeparam name="TAggregatePayloadOut"></typeparam>
/// <typeparam name="TEventPayload"> Need to put Event Payload's own type.</typeparam>
public interface
    IEventPayload<TAggregatePayloadIn, TAggregatePayloadOut, TEventPayload> : IEventPayloadApplicableTo<
    TAggregatePayloadIn>
    where TAggregatePayloadIn : IAggregatePayloadCommon
    where TEventPayload : IEventPayload<TAggregatePayloadIn, TAggregatePayloadOut, TEventPayload>
    where TAggregatePayloadOut : IAggregatePayloadGeneratable<TAggregatePayloadOut>
{
    public static abstract TAggregatePayloadOut OnEvent(TAggregatePayloadIn aggregatePayload, Event<TEventPayload> ev);
}
