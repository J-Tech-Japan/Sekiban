using Sekiban.Core.Event;

namespace Sekiban.Core.Types;

public static class EventTypesExtensions
{
    public static bool IsEventPayloadType(this Type eventPayloadType)
    {
        return eventPayloadType.DoesImplementingFromGenericInterfaceType(typeof(IEventPayload<>));
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
