using Sekiban.Core.Events;
namespace Sekiban.Core.Query.SingleProjections;

public interface ISingleProjectionEventApplicable<TProjectionPayload> : ISingleProjectionPayloadCommon
    where TProjectionPayload : ISingleProjectionPayloadCommon
{

#if NET7_0_OR_GREATER
    static abstract Func<TProjectionPayload, TProjectionPayload>? GetApplyEventFunc<TEventPayload>(
        Event<TEventPayload> ev) where TEventPayload : IEventPayloadCommon;
#else
    Func<TProjectionPayload, TProjectionPayload>? GetApplyEventFuncInstance<TEventPayload>(
        Event<TEventPayload> ev) where TEventPayload : IEventPayloadCommon;
#endif
}
