using Sekiban.Core.Events;
namespace Sekiban.Core.Types;

public static class EventTypesExtensions
{

    public static bool IsEventPayloadType(this Type eventPayloadType) =>
        eventPayloadType.DoesImplementingFromGenericInterfaceType(typeof(IEventPayload<,,>));

    public static bool IsEventConvertingPayloadType(this Type eventPayloadType) =>
        eventPayloadType.DoesImplementingFromGenericInterfaceType(typeof(IEventPayloadConvertingTo<>));

    public static Type GetEventConvertingPayloadConvertingType(this Type eventPayloadType)
    {
        var baseType = eventPayloadType.GetImplementingFromGenericInterfaceType(typeof(IEventPayloadConvertingTo<>));
        return baseType.GenericTypeArguments[0];
    }

    public static Type GetAggregatePayloadTypeFromEventPayload(this Type query)
    {
        var baseType = query.GetImplementingFromGenericInterfaceType(typeof(IEventPayload<,,>));
        return baseType.GenericTypeArguments[0];
    }
    public static Type GetEventPayloadTypeFromEventPayload(this Type query)
    {
        var baseType = query.GetImplementingFromGenericInterfaceType(typeof(IEventPayload<,,>));
        return baseType.GenericTypeArguments[(int)EventGenericTypeContents.EventPayload];
    }



    public static Type GetAggregatePayloadType(this IEvent ev)
    {
        var baseType = ev.GetPayload().GetType().GetImplementingFromGenericInterfaceType(typeof(IEventPayload<,,>));
        return baseType.GenericTypeArguments[0];
    }
    public static Type GetEventPayloadType(this IEvent ev)
    {
        var baseType = ev.GetPayload().GetType().GetImplementingFromGenericInterfaceType(typeof(IEventPayload<,,>));
        return baseType.GenericTypeArguments[(int)EventGenericTypeContents.EventPayload];
    }

    public static Type GetAggregatePayloadInType(this IEventPayloadCommon ev)
    {
        var baseType = ev.GetType().GetImplementingFromGenericInterfaceType(typeof(IEventPayload<,,>));
        return baseType.GenericTypeArguments[0];
    }
    public static Type GetAggregatePayloadOutType(this IEventPayloadCommon ev)
    {
        var baseType = ev.GetType().GetImplementingFromGenericInterfaceType(typeof(IEventPayload<,,>));
        return baseType.GenericTypeArguments[1];
    }
    public static Type GetEventPayloadType(this IEventPayloadCommon ev)
    {
        var baseType = ev.GetType().GetImplementingFromGenericInterfaceType(typeof(IEventPayload<,,>));
        return baseType.GenericTypeArguments[(int)EventGenericTypeContents.EventPayload];
    }
    public static Type GetEventAction(this IEventPayloadCommon ev)
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
