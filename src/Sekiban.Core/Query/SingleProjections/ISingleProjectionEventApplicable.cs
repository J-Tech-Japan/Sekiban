using Sekiban.Core.Events;
namespace Sekiban.Core.Query.SingleProjections;

public interface ISingleProjectionEventApplicable<TProjectionPayload> : ISingleProjectionPayloadCommon
    where TProjectionPayload : ISingleProjectionPayloadCommon
{

#if NET7_0_OR_GREATER
    static abstract TProjectionPayload? ApplyEvent<TEventPayload>(
        TProjectionPayload projectionPayload,
        Event<TEventPayload> ev) where TEventPayload : IEventPayloadCommon;
#else
    TProjectionPayload? ApplyEventInstance<TEventPayload>(TProjectionPayload projectionPayload, Event<TEventPayload> ev)
        where TEventPayload : IEventPayloadCommon;
#endif
}
