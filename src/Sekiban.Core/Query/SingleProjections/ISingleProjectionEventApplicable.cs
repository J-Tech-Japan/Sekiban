using Sekiban.Core.Events;
namespace Sekiban.Core.Query.SingleProjections;

public interface ISingleProjectionEventApplicable<TProjectionPayload> : ISingleProjectionPayloadCommon
    where TProjectionPayload : ISingleProjectionPayloadCommon
{

#if NET7_0_OR_GREATER
    static abstract Func<TProjectionPayload>? GetApplyEventFunc<TEventPayload>(
        TProjectionPayload projectionPayload,
        Event<TEventPayload> ev) where TEventPayload : IEventPayloadCommon;
#else
    Func<TProjectionPayload>? GetApplyEventFuncInstance<TEventPayload>(
        TProjectionPayload projectionPayload, Event<TEventPayload> ev) where TEventPayload : IEventPayloadCommon;
#endif
}
