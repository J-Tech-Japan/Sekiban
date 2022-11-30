using Sekiban.Core.Event;
using Sekiban.Core.Exceptions;
using System.Reflection;
namespace Sekiban.Core.Aggregate;

public static class EventHelper
{
    public static IEvent HandleEvent<TAggregatePayload>(Aggregate<TAggregatePayload> aggregate, IEventPayloadCommon eventPayload)
        where TAggregatePayload : IAggregatePayload, new()
    {
        var aggregateType = aggregate.GetType();
        var methodName = nameof(Aggregate<TAggregatePayload>.AddAndApplyEvent);
        var aggregateMethodBase = aggregateType.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
        var aggregateMethod = aggregateMethodBase?.MakeGenericMethod(eventPayload.GetType());
        return aggregateMethod?.Invoke(aggregate, new object?[] { eventPayload }) as IEvent ??
            throw new SekibanEventFailedToActivateException();
    }

    public static IEvent GenerateEventToSave<TEventPayload, TAggregatePayload>(Guid aggregateId, TEventPayload payload)
        where TEventPayload : IEventPayload<TAggregatePayload> where TAggregatePayload : IAggregatePayload, new()
    {
        var eventPayloadType = payload.GetType();
        // ReSharper disable once SuspiciousTypeConversion.Global
        var eventBaseType = typeof(Event<>);
        var eventType = eventBaseType.MakeGenericType(eventPayloadType);
        return Activator.CreateInstance(eventType, aggregateId, typeof(TAggregatePayload), payload) as IEvent ??
            throw new SekibanEventFailedToActivateException();
    }
}
