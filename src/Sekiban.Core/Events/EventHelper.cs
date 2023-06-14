using Sekiban.Core.Aggregate;
using Sekiban.Core.Exceptions;
using Sekiban.Core.Shared;
using Sekiban.Core.Types;
using System.Reflection;
namespace Sekiban.Core.Events;

public static class EventHelper
{
    public static IEvent HandleEvent<TAggregatePayload>(
        Aggregate<TAggregatePayload> aggregate,
        IEventPayloadCommon eventPayload,
        string rootPartitionKey) where TAggregatePayload : IAggregatePayloadCommon
    {
        var aggregateType = aggregate.GetType();
        var methodName = nameof(Aggregate<TAggregatePayload>.AddAndApplyEvent);
        var aggregateMethodBase = aggregateType.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
        var aggregateMethod = aggregateMethodBase?.MakeGenericMethod(eventPayload.GetType());
        return aggregateMethod?.Invoke(aggregate, new object?[] { eventPayload, rootPartitionKey }) as IEvent ??
            throw new SekibanEventFailedToActivateException();
    }

    public static IEvent GenerateEventToSave<TEventPayload, TAggregatePayload>(Guid aggregateId, string rootPartitionKey, TEventPayload payload)
        where TEventPayload : IEventPayloadApplicableTo<TAggregatePayload> where TAggregatePayload : IAggregatePayloadCommon
    {
        var eventPayloadType = payload.GetType();
        // ReSharper disable once SuspiciousTypeConversion.Global
        var eventBaseType = typeof(Event<>);
        var eventType = eventBaseType.MakeGenericType(eventPayloadType);
        return Activator.CreateInstance(eventType, aggregateId, typeof(TAggregatePayload), payload, rootPartitionKey) as IEvent ??
            throw new SekibanEventFailedToActivateException();
    }

    public static (IEvent, IEventPayloadCommon) GetConvertedEventAndPayloadIfConverted(IEvent ev, IEventPayloadCommon payload)
    {
        if (payload.GetType().IsEventConvertingPayloadType())
        {
            (ev, payload) = ev.GetConvertedEventAndPayload();
            if (payload.GetType().IsEventConvertingPayloadType())
            {
                return GetConvertedEventAndPayloadIfConverted(ev, payload);
            }
        }
        return (ev, payload);
    }

    public static IEvent? GetUnregisteredEvent(JsonElement dynamicObject)
    {
        var payload = dynamicObject.GetProperty(nameof(Event<UnregisteredEventPayload>.Payload));
        var payloadJson = SekibanJsonHelper.Serialize(payload);
        var eventPayload = new UnregisteredEventPayload
        {
            JsonString = payloadJson ?? string.Empty,
            EventTypeName = dynamicObject.GetProperty(nameof(Event<EmptyEventPayload>.DocumentTypeName)).GetString() ?? string.Empty
        };
        var ev
            = SekibanJsonHelper.Deserialize(SekibanJsonHelper.Serialize(dynamicObject), typeof(Event<EmptyEventPayload>)) as Event<EmptyEventPayload>;
        return ev?.ChangePayload(eventPayload);
    }
}
