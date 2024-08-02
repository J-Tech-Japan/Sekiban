using Sekiban.Core.Aggregate;
using Sekiban.Core.Exceptions;
using Sekiban.Core.Shared;
using Sekiban.Core.Types;
using System.Reflection;
namespace Sekiban.Core.Events;

/// <summary>
///     Event Helper for Generate, Convert and Not Registered Events
///     App Developers usually does not need to use this class.
/// </summary>
public static class EventHelper
{
    /// <summary>
    ///     handle events to add and apply for the aggregates
    /// </summary>
    /// <param name="aggregate"></param>
    /// <param name="eventPayload"></param>
    /// <param name="rootPartitionKey"></param>
    /// <typeparam name="TAggregatePayload"></typeparam>
    /// <returns></returns>
    /// <exception cref="SekibanEventFailedToActivateException"></exception>
    public static IEvent HandleEvent<TAggregatePayload>(
        Aggregate<TAggregatePayload> aggregate,
        IEventPayloadCommon eventPayload,
        string rootPartitionKey) where TAggregatePayload : IAggregatePayloadCommon
    {
        var aggregateType = aggregate.GetType();
        var methodName = nameof(Aggregate<TAggregatePayload>.AddAndApplyEvent);
        var aggregateMethodBase = aggregateType.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance);
        var aggregateMethod = aggregateMethodBase?.MakeGenericMethod(eventPayload.GetType());
        return aggregateMethod?.Invoke(aggregate, [eventPayload, rootPartitionKey]) as IEvent ??
            throw new SekibanEventFailedToActivateException();
    }
    /// <summary>
    ///     Generate Event from payload, aggregateId and rootPartitionKey
    /// </summary>
    /// <param name="aggregateId"></param>
    /// <param name="rootPartitionKey"></param>
    /// <param name="payload"></param>
    /// <typeparam name="TEventPayload"></typeparam>
    /// <typeparam name="TAggregatePayload"></typeparam>
    /// <returns></returns>
    /// <exception cref="SekibanEventFailedToActivateException"></exception>
    public static IEvent GenerateEventToSave<TEventPayload, TAggregatePayload>(
        Guid aggregateId,
        string rootPartitionKey,
        TEventPayload payload) where TEventPayload : IEventPayloadApplicableTo<TAggregatePayload>
        where TAggregatePayload : IAggregatePayloadCommon
    {
        var eventPayloadType = payload.GetType();
        // ReSharper disable once SuspiciousTypeConversion.Global
        var eventBaseType = typeof(Event<>);
        var eventType = eventBaseType.MakeGenericType(eventPayloadType);
        return Activator.CreateInstance(
                eventType,
                aggregateId,
                typeof(TAggregatePayload),
                payload,
                rootPartitionKey) as IEvent ??
            throw new SekibanEventFailedToActivateException();
    }
    /// <summary>
    ///     Get Converted event
    /// </summary>
    /// <param name="ev"></param>
    /// <param name="payload"></param>
    /// <returns></returns>
    public static (IEvent, IEventPayloadCommon) GetConvertedEventAndPayloadIfConverted(
        IEvent ev,
        IEventPayloadCommon payload)
    {
        if (payload.GetType().IsEventConvertingPayloadType())
        {
            var (evConverted, payloadConverted) = ev.GetConvertedEventAndPayload();
            if (payload != payloadConverted && payloadConverted.GetType().IsEventConvertingPayloadType())
            {
                return GetConvertedEventAndPayloadIfConverted(evConverted, payloadConverted);
            }
        }
        return (ev, payload);
    }
    /// <summary>
    ///     Get Unregistered events
    /// </summary>
    /// <param name="dynamicObject"></param>
    /// <returns></returns>
    public static IEvent? GetUnregisteredEvent(JsonElement dynamicObject)
    {
        var payload = dynamicObject.GetProperty(nameof(Event<UnregisteredEventPayload>.Payload));
        var payloadJson = SekibanJsonHelper.Serialize(payload);
        var eventPayload = new UnregisteredEventPayload
        {
            JsonString = payloadJson ?? string.Empty,
            EventTypeName = dynamicObject.GetProperty(nameof(Event<EmptyEventPayload>.DocumentTypeName)).GetString() ??
                string.Empty
        };
        var ev = SekibanJsonHelper.Deserialize(
            SekibanJsonHelper.Serialize(dynamicObject),
            typeof(Event<EmptyEventPayload>)) as Event<EmptyEventPayload>;
        return ev?.ChangePayload(eventPayload);
    }
}
