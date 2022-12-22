using Sekiban.Core.Events;
namespace Sekiban.Core.Types;

public static class EventTypesExtensions
{
    public static bool IsEventPayloadType(this Type eventPayloadType) =>
        eventPayloadType.DoesImplementingFromGenericInterfaceType(typeof(IEventPayload<>));

    public static bool IsEventConvertingPayloadType(this Type eventPayloadType) =>
        eventPayloadType.DoesImplementingFromGenericInterfaceType(typeof(IEventPayloadConvertingTo<>));

    public static Type GetEventConvertingPayloadConvertingType(this Type eventPayloadType)
    {
        var baseType = eventPayloadType.GetImplementingFromGenericInterfaceType(typeof(IEventPayloadConvertingTo<>));
        return baseType.GenericTypeArguments[0];
    }
    public static Type GetEventConvertingToType(this Type eventPayloadType)
    {
        var baseType = eventPayloadType.GetImplementingFromGenericInterfaceType(typeof(IEventPayload<>));
        var convertingType = baseType.GenericTypeArguments[0];
        return baseType.MakeGenericType(convertingType);
    }

    public static Type GetAggregatePayloadTypeFromEventPayload(this Type query)
    {
        var baseType = query.GetImplementingFromGenericInterfaceType(typeof(IEventPayload<>));
        return baseType.GenericTypeArguments[0];
    }

    public static Type GetAggregatePayloadType(this IEvent ev)
    {
        var baseType = ev.GetPayload().GetType().GetImplementingFromGenericInterfaceType(typeof(IEventPayload<>));
        return baseType.GenericTypeArguments[0];
    }

    public static Type GetAggregatePayloadType(this IEventPayloadCommon ev)
    {
        var baseType = ev.GetType().GetImplementingFromGenericInterfaceType(typeof(IEventPayload<>));
        return baseType.GenericTypeArguments[0];
    }
}
