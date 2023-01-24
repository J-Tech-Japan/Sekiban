using Sekiban.Core.Events;
namespace Sekiban.Core.Query.MultiProjections;

public interface IMultiProjectionPayload<TProjectionPayload> : IMultiProjectionPayloadCommon
    where TProjectionPayload : IMultiProjectionPayloadCommon
{
    public TargetAggregatePayloadCollection GetTargetAggregatePayloads() => new();
#if NET7_0_OR_GREATER
    public static abstract TProjectionPayload? ApplyEvent<TEventPayload>(
        TProjectionPayload projectionPayload,
        Event<TEventPayload> ev) where TEventPayload : IEventPayloadCommon;
#else
    public TProjectionPayload? ApplyEventInstance<TEventPayload>(
        TProjectionPayload projectionPayload,
        Event<TEventPayload> ev) where TEventPayload : IEventPayloadCommon;
#endif
}
