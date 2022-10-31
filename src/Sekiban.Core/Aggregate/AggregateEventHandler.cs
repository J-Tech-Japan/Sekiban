using Sekiban.Core.Event;
using Sekiban.Core.Exceptions;
using System.Reflection;
namespace Sekiban.Core.Aggregate;

public static class AggregateEventHandler
{
    public static IEvent HandleAggregateEvent<TAggregatePayload>(Aggregate<TAggregatePayload> aggregate, IEventPayload eventPayload)
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
        where TEventPayload : IChangedEvent<TAggregatePayload> where TAggregatePayload : IAggregatePayload, new()
    {
        var eventPayloadType = payload.GetType();
        // ReSharper disable once SuspiciousTypeConversion.Global
        var isCreatedEvent = eventPayloadType is ICreatedEventPayload;
        var aggregateEventBaseType = typeof(Event<>);
        var aggregateEventType = aggregateEventBaseType.MakeGenericType(eventPayloadType);
        return Activator.CreateInstance(aggregateEventType, aggregateId, typeof(TAggregatePayload), payload, isCreatedEvent) as IEvent ??
            throw new SekibanEventFailedToActivateException();
    }
}
