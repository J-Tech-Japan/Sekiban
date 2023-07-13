using Sekiban.Core.Events;
using System.Reflection;
namespace Sekiban.Core.Query.MultiProjections;

public interface IMultiProjectionPayload<TProjectionPayload> : IMultiProjectionPayloadCommon where TProjectionPayload : IMultiProjectionPayloadCommon
{
    public TargetAggregatePayloadCollection GetTargetAggregatePayloads() => new();
    public static abstract TProjectionPayload? ApplyEvent<TEventPayload>(TProjectionPayload projectionPayload, Event<TEventPayload> ev)
        where TEventPayload : IEventPayloadCommon;
    public TProjectionPayload ApplyIEvent(IEvent ev)
    {
        var payloadType = ev.GetPayload().GetType();
        var method = typeof(TProjectionPayload).GetMethod(nameof(ApplyEvent), BindingFlags.Static | BindingFlags.Public);
        var genericMethod = method?.MakeGenericMethod(payloadType);
        return (TProjectionPayload?)genericMethod?.Invoke(typeof(TProjectionPayload), new object?[] { this, ev }) ?? (TProjectionPayload)this;
    }
}
