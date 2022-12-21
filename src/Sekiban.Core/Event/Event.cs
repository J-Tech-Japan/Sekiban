using Sekiban.Core.Document;
using Sekiban.Core.History;
using Sekiban.Core.Partition;
using Sekiban.Core.Types;
namespace Sekiban.Core.Event;

public record Event<TEventPayload> : Document.Document, IEvent where TEventPayload : IEventPayloadCommon
{
    public Event()
    {
    }

    public Event(Guid aggregateId, Type aggregateType, TEventPayload eventPayload) : base(
        aggregateId,
        PartitionKeyGenerator.ForEvent(aggregateId, aggregateType),
        DocumentType.Event,
        typeof(TEventPayload).Name)
    {
        Payload = eventPayload;
        AggregateType = aggregateType.Name;
    }

    public TEventPayload Payload { get; init; } = default!;

    public string AggregateType { get; init; } = null!;

    /// <summary>
    ///     集約のイベント適用後のバージョン
    /// </summary>
    public int Version { get; init; }

    public List<CallHistory> CallHistories { get; init; } = new();

    public IEventPayloadCommon GetPayload() => Payload;

    public T? GetPayload<T>() where T : class, IEventPayloadCommon => Payload as T;

    public (IEvent, IEventPayloadCommon) GetConvertedEventAndPayload()
    {
        var payload = GetPayload();
        if (payload.GetType().IsEventConvertingPayloadType())
        {
            var method = payload.GetType().GetMethod("ConvertTo");
            var convertedPayload = (dynamic?)method?.Invoke(payload, new object?[] { });
            if (convertedPayload is not null)
            {
                var convertedType = payload.GetType().GetEventConvertingPayloadConvertingType();
                var changeEventMethod = GetType().GetMethod("ChangePayload");
                var genericMethod = changeEventMethod?.MakeGenericMethod(convertedType);
                var convertedEvent = (dynamic?)genericMethod?.Invoke(this, new object?[] { convertedPayload });
                if (convertedEvent is not null)
                {
                    return (convertedEvent, convertedPayload);
                }
            }
        }
        return (this, payload);
    }


    public List<CallHistory> GetCallHistoriesIncludesItself()
    {
        var histories = new List<CallHistory>();
        histories.AddRange(CallHistories);
        histories.Add(new CallHistory(Id, GetPayload().GetType().Name, string.Empty));
        return histories;
    }

    public static Event<TEventPayload> GenerateEvent(Guid aggregateId, Type aggregateType, TEventPayload eventPayload) =>
        new(aggregateId, aggregateType, eventPayload);

    public Event<TNewPayload> ChangePayload<TNewPayload>(TNewPayload newPayload) where TNewPayload : IEventPayloadCommon => new()
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
