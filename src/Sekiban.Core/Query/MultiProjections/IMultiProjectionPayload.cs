using Sekiban.Core.Events;
using System.Reflection;
namespace Sekiban.Core.Query.MultiProjections;

/// <summary>
///     Interface for MultiProjectionPayload
/// </summary>
/// <typeparam name="TProjectionPayload"></typeparam>
public interface IMultiProjectionPayload<TProjectionPayload> : IMultiProjectionPayloadCommon where TProjectionPayload : IMultiProjectionPayloadCommon
{
    /// <summary>
    ///     MultiProjection can set which AggregateType to use for the projection.
    ///     Setting proper AggregateType will make it easier to use the projection.
    /// </summary>
    /// <returns></returns>
    public TargetAggregatePayloadCollection GetTargetAggregatePayloads() => new();
    /// <summary>
    ///     Apply Event to the projection.
    ///     Each Projection Payload should Implement this method.
    ///     Event is typed with Generic, so you can use pattern matching to get the payload.
    /// </summary>
    /// <param name="projectionPayload"></param>
    /// <param name="ev"></param>
    /// <typeparam name="TEventPayload"></typeparam>
    /// <returns></returns>
    public static abstract TProjectionPayload? ApplyEvent<TEventPayload>(TProjectionPayload projectionPayload, Event<TEventPayload> ev)
        where TEventPayload : IEventPayloadCommon;
    /// <summary>
    ///     General Apply Event method. usually it does not need to override this method.
    /// </summary>
    /// <param name="ev"></param>
    /// <returns></returns>
    public TProjectionPayload ApplyIEvent(IEvent ev)
    {
        var payloadType = ev.GetPayload().GetType();
        var method = typeof(TProjectionPayload).GetMethod(nameof(ApplyEvent), BindingFlags.Static | BindingFlags.Public);
        var genericMethod = method?.MakeGenericMethod(payloadType);
        return (TProjectionPayload?)genericMethod?.Invoke(typeof(TProjectionPayload), new object?[] { this, ev }) ?? (TProjectionPayload)this;
    }
    /// <summary>
    ///     Generate Default Payload in Static.
    /// </summary>
    /// <returns></returns>
    public static abstract TProjectionPayload CreateInitialPayload();
}
