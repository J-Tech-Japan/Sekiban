using Sekiban.Core.Event;
namespace Sekiban.Core.Types;

public static class EventTypesExtensions
{
    public static bool IsEventPayloadType(this Type eventPayloadType) =>
        eventPayloadType.DoesImplementingFromGenericInterfaceType(typeof(IApplicableEvent<>));
    public static Type GetAggregatePayloadTypeFromEventPayload(this Type query)
    {
        var baseType = query.GetImplementingFromGenericInterfaceType(typeof(IApplicableEvent<>));
        return baseType.GenericTypeArguments[0];
    }
    public static Type GetAggregatePayloadType(this IEvent ev)
    {
        var baseType = ev.GetPayload().GetType().GetImplementingFromGenericInterfaceType(typeof(IApplicableEvent<>));
        return baseType.GenericTypeArguments[0];
    }
    public static Type GetAggregatePayloadType(this IEventPayload ev)
    {
        var baseType = ev.GetType().GetImplementingFromGenericInterfaceType(typeof(IApplicableEvent<>));
        return baseType.GenericTypeArguments[0];
    }
}
