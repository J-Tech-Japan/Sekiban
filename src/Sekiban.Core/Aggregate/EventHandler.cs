using Sekiban.Core.Event;
using Sekiban.Core.Exceptions;
using System.Reflection;
namespace Sekiban.Core.Aggregate;

public static class EventHandler
{
    public static IEvent HandleEvent<TAggregatePayload>(AggregateIdentifier<TAggregatePayload> aggregateIdentifier, IEventPayload eventPayload)
        where TAggregatePayload : IAggregatePayload, new()
    {
        var aggregateType = aggregateIdentifier.GetType();
        var methodName = nameof(AggregateIdentifier<TAggregatePayload>.AddAndApplyEvent);
        var aggregateMethodBase = aggregateType.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
        var aggregateMethod = aggregateMethodBase?.MakeGenericMethod(eventPayload.GetType());
        return aggregateMethod?.Invoke(aggregateIdentifier, new object?[] { eventPayload }) as IEvent ??
            throw new SekibanEventFailedToActivateException();
    }

    public static IEvent GenerateEventToSave<TEventPayload, TAggregatePayload>(Guid aggregateId, TEventPayload payload)
        where TEventPayload : IChangedEvent<TAggregatePayload> where TAggregatePayload : IAggregatePayload, new()
    {
        var eventPayloadType = payload.GetType();
        // ReSharper disable once SuspiciousTypeConversion.Global
        var isCreatedEvent = eventPayloadType is ICreatedEventPayload;
        var eventBaseType = typeof(Event<>);
        var eventType = eventBaseType.MakeGenericType(eventPayloadType);
        return Activator.CreateInstance(eventType, aggregateId, typeof(TAggregatePayload), payload, isCreatedEvent) as IEvent ??
            throw new SekibanEventFailedToActivateException();
    }
}
