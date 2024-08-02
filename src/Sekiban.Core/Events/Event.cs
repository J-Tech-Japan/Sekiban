using Sekiban.Core.Documents;
using Sekiban.Core.History;
using Sekiban.Core.Partition;
using Sekiban.Core.Types;
using System.Reflection;
namespace Sekiban.Core.Events;

/// <summary>
///     Event object. This class is used for all types of events.
///     Use Generics to specify payload type.
/// </summary>
/// <typeparam name="TEventPayload"></typeparam>
public record Event<TEventPayload> : Document, IEvent where TEventPayload : IEventPayloadCommon
{
    /// <summary>
    ///     Payload object.
    /// </summary>
    public TEventPayload Payload { get; init; } = default!;
    public Event()
    {
    }

    public Event(Guid aggregateId, Type aggregateType, TEventPayload eventPayload, string rootPartitionKey) : base(
        aggregateId,
        PartitionKeyGenerator.ForEvent(aggregateId, aggregateType, rootPartitionKey),
        DocumentType.Event,
        typeof(TEventPayload).Name,
        aggregateType.Name,
        rootPartitionKey) =>
        Payload = eventPayload;

    /// <summary>
    ///     Version after applying the aggregate event.
    /// </summary>
    public int Version { get; init; }
    /// <summary>
    ///     Event Call histories
    /// </summary>
    public List<CallHistory> CallHistories { get; init; } = [];
    /// <summary>
    ///     Get Payload object
    /// </summary>
    /// <returns></returns>
    public IEventPayloadCommon GetPayload() => Payload;
    /// <summary>
    ///     Get Event Payload object by Type
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public T? GetPayload<T>() where T : class, IEventPayloadCommon => Payload as T;
    /// <summary>
    ///     Convert Event to new version
    ///     Event payload need to implements
    ///     <see cref="IEventPayloadConvertingTo{TNewEventPayload}" />
    /// </summary>
    /// <returns></returns>
    public (IEvent, IEventPayloadCommon) GetConvertedEventAndPayload()
    {
        var payload = GetPayload();
        if (payload.GetType().IsEventConvertingPayloadType())
        {
            var method = payload.GetType().GetConvertToMethod();
            var convertedPayload = (dynamic?)method?.Invoke(payload, []);
            if (convertedPayload is not null)
            {
                var convertedType = payload.GetType().GetEventConvertingPayloadConvertingType();
                var changeEventMethod = GetType().GetMethod(nameof(Event<IEventPayloadCommon>.ChangePayload));
                var genericMethod = changeEventMethod?.MakeGenericMethod(convertedType);
                var convertedEvent = (dynamic?)genericMethod?.Invoke(this, [convertedPayload]);
                if (convertedEvent is not null)
                {
                    return (convertedEvent, convertedPayload);
                }
            }
        }
        return (this, payload);
    }

    /// <summary>
    ///     Add current history nd add this event for next step
    /// </summary>
    /// <returns></returns>
    public List<CallHistory> GetCallHistoriesIncludesItself()
    {
        var histories = new List<CallHistory>();
        histories.AddRange(CallHistories);
        histories.Add(new CallHistory(Id, GetPayload().GetType().Name, string.Empty));
        return histories;
    }
    /// <summary>
    ///     Generate event object
    /// </summary>
    /// <param name="aggregateId"></param>
    /// <param name="aggregateType"></param>
    /// <param name="eventPayload"></param>
    /// <param name="rootPartitionKey"></param>
    /// <returns></returns>
    public static Event<TEventPayload> GenerateEvent(Guid aggregateId, Type aggregateType, TEventPayload eventPayload, string rootPartitionKey) =>
        new(aggregateId, aggregateType, eventPayload, rootPartitionKey);
    /// <summary>
    ///     change payload to converted payload and make new event object
    /// </summary>
    /// <param name="newPayload"></param>
    /// <typeparam name="TNewPayload"></typeparam>
    /// <returns></returns>
    public Event<TNewPayload> ChangePayload<TNewPayload>(TNewPayload newPayload) where TNewPayload : IEventPayloadCommon =>
        new()
        {
            Id = Id,
            AggregateId = AggregateId,
            PartitionKey = PartitionKey,
            DocumentType = DocumentType,
            DocumentTypeName = typeof(TNewPayload).Name,
            TimeStamp = TimeStamp,
            SortableUniqueId = SortableUniqueId,
            Payload = newPayload,
            AggregateType = AggregateType,
            Version = Version,
            CallHistories = CallHistories
        };
}
public static class Event
{
    public static Event<TEventPayload> GenerateEvent<TEventPayload>(
        Guid id,
        Guid aggregateId,
        string partitionKey,
        DocumentType documentType,
        string documentTypeName,
        DateTime timeStamp,
        string sortableUniqueId,
        TEventPayload eventPayload,
        string aggregateType,
        int version,
        string rootPartitionKey,
        List<CallHistory> callHistories) where TEventPayload : IEventPayloadCommon =>
        new()
        {
            Id = id,
            AggregateId = aggregateId,
            PartitionKey = partitionKey,
            DocumentType = documentType,
            DocumentTypeName = documentTypeName,
            TimeStamp = timeStamp,
            SortableUniqueId = sortableUniqueId,
            Payload = eventPayload,
            AggregateType = aggregateType,
            Version = version,
            RootPartitionKey = rootPartitionKey,
            CallHistories = callHistories
        };
    public static IEvent? GenerateIEvent(
        Guid id,
        Guid aggregateId,
        string partitionKey,
        DocumentType documentType,
        string documentTypeName,
        Type eventPayloadType,
        DateTime timeStamp,
        string sortableUniqueId,
        IEventPayloadCommon eventPayload,
        string aggregateType,
        int version,
        string rootPartitionKey,
        List<CallHistory> callHistories) =>
        typeof(Event).GetMethod(nameof(GenerateEvent), BindingFlags.Public | BindingFlags.Static)
            ?.MakeGenericMethod(eventPayload.GetType())
            .Invoke(
                null,
                new object[]
                {
                    id,
                    aggregateId,
                    partitionKey,
                    documentType,
                    documentTypeName,
                    timeStamp,
                    sortableUniqueId,
                    eventPayload,
                    aggregateType,
                    version,
                    rootPartitionKey,
                    callHistories
                }) as IEvent;
}
