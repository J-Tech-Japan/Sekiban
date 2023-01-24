using Sekiban.Core.Events;
namespace Sekiban.Core.Query.MultiProjections;

public interface IMultiProjectionPayload<TProjectionPayload> : IMultiProjectionPayloadCommon
    where TProjectionPayload : IMultiProjectionPayloadCommon
{
    public TargetAggregatePayloadCollection GetTargetAggregatePayloads() => new();
#if NET7_0_OR_GREATER
    public static abstract Func<TProjectionPayload>? GetApplyEventFunc<TEventPayload>(
        TProjectionPayload projectionPayload,
        Event<TEventPayload> ev) where TEventPayload : IEventPayloadCommon;
#else
    public Func<TProjectionPayload>? GetApplyEventFuncInstance<TEventPayload>(
        TProjectionPayload projectionPayload,
        Event<TEventPayload> ev) where TEventPayload : IEventPayloadCommon;
#endif
}
