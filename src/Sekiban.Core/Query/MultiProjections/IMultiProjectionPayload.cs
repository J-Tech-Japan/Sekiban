using Sekiban.Core.Events;
using System.Reflection;
namespace Sekiban.Core.Query.MultiProjections;

public interface IMultiProjectionPayload<TProjectionPayload> : IMultiProjectionPayloadCommon
    where TProjectionPayload : IMultiProjectionPayloadCommon
{
    public TargetAggregatePayloadCollection GetTargetAggregatePayloads()
    {
        return new TargetAggregatePayloadCollection();
    }
#if NET7_0_OR_GREATER
    public static abstract TProjectionPayload? ApplyEvent<TEventPayload>(
        TProjectionPayload projectionPayload,
        Event<TEventPayload> ev) where TEventPayload : IEventPayloadCommon;
#else
    public TProjectionPayload? ApplyEventInstance<TEventPayload>(
        TProjectionPayload projectionPayload,
        Event<TEventPayload> ev) where TEventPayload : IEventPayloadCommon;
#endif
    public TProjectionPayload ApplyIEvent(
        IEvent ev)
    {
        var payloadType = ev.GetPayload().GetType();
#if NET7_0_OR_GREATER
        var method = typeof(TProjectionPayload).GetMethod(
            nameof(ApplyEvent),
            BindingFlags.Static | BindingFlags.Public);
        var genericMethod = method?.MakeGenericMethod(payloadType);
        return (TProjectionPayload?)genericMethod?.Invoke(typeof(TProjectionPayload), new object?[] { this, ev }) ??
            (TProjectionPayload)this;
#else
        var method = GetType()
            .GetMethod(
                "ApplyEventInstance",
                BindingFlags.Instance | BindingFlags.Public);
        var genericMethod = method?.MakeGenericMethod(payloadType);
        return (TProjectionPayload?)genericMethod?.Invoke(this, new object?[] { this, ev }) ??
            (TProjectionPayload)this;
#endif
    }
}
