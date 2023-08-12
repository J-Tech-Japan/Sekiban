using Sekiban.Core.Events;
namespace Sekiban.Core.Types;

/// <summary>
///     Event Types Extensions.
/// </summary>
public static class EventTypesExtensions
{

    /// <summary>
    ///     Check if the type is Event Payload Type.
    /// </summary>
    /// <param name="eventPayloadType"></param>
    /// <returns></returns>
    public static bool IsEventPayloadType(this Type eventPayloadType) =>
        eventPayloadType.DoesImplementingFromGenericInterfaceType(typeof(IEventPayload<,,>));
    /// <summary>
    ///     Check if the given type needs to be converted to another type.
    /// </summary>
    /// <param name="eventPayloadType"></param>
    /// <returns></returns>
    public static bool IsEventConvertingPayloadType(this Type eventPayloadType) =>
        eventPayloadType.DoesImplementingFromGenericInterfaceType(typeof(IEventPayloadConvertingTo<>));

    /// <summary>
    ///     Get the type to which the given type is converted.
    /// </summary>
    /// <param name="eventPayloadType"></param>
    /// <returns></returns>
    public static Type GetEventConvertingPayloadConvertingType(this Type eventPayloadType)
    {
        var baseType = eventPayloadType.GetImplementingFromGenericInterfaceType(typeof(IEventPayloadConvertingTo<>));
        return baseType.GenericTypeArguments[0];
    }
    /// <summary>
    ///     Get aggregate payload type from event payload type
    /// </summary>
    /// <param name="query"></param>
    /// <returns></returns>
    public static Type GetAggregatePayloadTypeFromEventPayload(this Type query)
    {
        var baseType = query.GetImplementingFromGenericInterfaceType(typeof(IEventPayload<,,>));
        return baseType.GenericTypeArguments[0];
    }
    /// <summary>
    ///     Get event payload type from event payload type
    /// </summary>
    /// <param name="query"></param>
    /// <returns></returns>
    public static Type GetEventPayloadTypeFromEventPayload(this Type query)
    {
        var baseType = query.GetImplementingFromGenericInterfaceType(typeof(IEventPayload<,,>));
        return baseType.GenericTypeArguments[(int)EventGenericTypeContents.EventPayload];
    }
    /// <summary>
    ///     Get Aggregate payload type from given <see cref="Event{TEventPayload}" />
    /// </summary>
    /// <param name="ev"></param>
    /// <returns></returns>
    public static Type GetAggregatePayloadType(this IEvent ev)
    {
        var baseType = ev.GetPayload().GetType().GetImplementingFromGenericInterfaceType(typeof(IEventPayload<,,>));
        return baseType.GenericTypeArguments[0];
    }
    /// <summary>
    ///     Get Aggregate payload type from given <see cref="Event{TEventPayload}" />
    /// </summary>
    /// <param name="ev"></param>
    /// <returns></returns>
    public static Type GetEventPayloadType(this IEvent ev)
    {
        var baseType = ev.GetPayload().GetType().GetImplementingFromGenericInterfaceType(typeof(IEventPayload<,,>));
        return baseType.GenericTypeArguments[(int)EventGenericTypeContents.EventPayload];
    }
    /// <summary>
    ///     Get Aggregate payload type from given Event Payload <see cref="IEventPayloadCommon" />
    /// </summary>
    /// <param name="ev"></param>
    /// <returns></returns>
    public static Type GetAggregatePayloadInType(this IEventPayloadCommon ev)
    {
        var baseType = ev.GetType().GetImplementingFromGenericInterfaceType(typeof(IEventPayload<,,>));
        return baseType.GenericTypeArguments[0];
    }
    /// <summary>
    ///     Get Aggregate payload type that become after given events.
    ///     When event payload is using Aggregate subtypes, Aggregate type that starts event payload and ends event payload is
    ///     different.
    /// </summary>
    /// <param name="ev"></param>
    /// <returns></returns>
    public static Type GetAggregatePayloadOutType(this IEventPayloadCommon ev)
    {
        var baseType = ev.GetType().GetImplementingFromGenericInterfaceType(typeof(IEventPayload<,,>));
        return baseType.GenericTypeArguments[1];
    }
    /// <summary>
    ///     Get Event Payload type from given Event Payload <see cref="IEventPayloadCommon" />
    /// </summary>
    /// <param name="ev"></param>
    /// <returns></returns>
    public static Type GetEventPayloadType(this IEventPayloadCommon ev)
    {
        var baseType = ev.GetType().GetImplementingFromGenericInterfaceType(typeof(IEventPayload<,,>));
        return baseType.GenericTypeArguments[(int)EventGenericTypeContents.EventPayload];
    }
    private enum EventGenericTypeContents
    {
        // ReSharper disable once UnusedMember.Local
        AggregatePayloadIn,
        // ReSharper disable once UnusedMember.Local
        AggregatePayloadOut,
        EventPayload
    }
}
