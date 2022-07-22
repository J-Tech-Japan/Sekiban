using Newtonsoft.Json;
using System.Runtime.Serialization;
namespace Sekiban.EventSourcing.AggregateEvents;

[SekibanEventType]
public record AggregateEvent<TEventPayload> : IAggregateEvent where TEventPayload : IEventPayload
{
    public AggregateEvent(Guid aggregateId, TEventPayload payload, Type aggregateTypeObject, bool isAggregateInitialEvent = false)
    {
        Id = Guid.NewGuid();
        DocumentType = DocumentType.AggregateEvent;
        DocumentTypeName = typeof(TEventPayload).Name;
        TimeStamp = DateTime.UtcNow;
        SortableUniqueId = SortableUniqueIdGenerator.Generate(TimeStamp, Id);
        AggregateId = aggregateId;
        Payload = payload;
        AggregateType = aggregateTypeObject.Name;
        var partitionKeyFactory = new AggregateIdPartitionKeyFactory(aggregateId, aggregateTypeObject);
        PartitionKey = partitionKeyFactory.GetPartitionKey(DocumentType);
        IsAggregateInitialEvent = isAggregateInitialEvent;
    }

    [JsonConstructor]
    protected AggregateEvent() { }

    [JsonProperty("id")]
    [DataMember]
    public Guid Id { get; init; }
    [DataMember]
    public string PartitionKey { get; init; }

    [DataMember]
    public DocumentType DocumentType { get; init; }
    [DataMember]
    public string DocumentTypeName { get; init; } = null!;
    [DataMember]
    public DateTime TimeStamp { get; init; }
    [DataMember]
    public string SortableUniqueId { get; init; } = string.Empty;
    [DataMember]
    public Guid AggregateId { get; init; }
    [DataMember]
    public string AggregateType { get; init; } = null!;

    /// <summary>
    ///     集約のスタートイベントの場合はtrueにする。
    /// </summary>
    [DataMember]
    public bool IsAggregateInitialEvent { get; init; }

    /// <summary>
    ///     集約のイベント適用後のバージョン
    /// </summary>
    [DataMember]
    public int Version { get; init; }

    [DataMember]
    public IEventPayload Payload { get; init; }

    [DataMember]
    public List<CallHistory> CallHistories { get; init; } = new();

    public dynamic GetComparableObject(IAggregateEvent original, bool copyVersion = true) =>
        this with
        {
            Version = copyVersion ? original.Version : Version,
            SortableUniqueId = original.SortableUniqueId,
            CallHistories = original.CallHistories,
            Id = original.Id,
            TimeStamp = original.TimeStamp
        };

    public static AggregateEvent<TEventPayload> CreatedEvent(Guid aggregateId, TEventPayload payload, Type aggregateType) =>
        new(aggregateId, payload, aggregateType, true);
    public static AggregateEvent<TEventPayload> ChangedEvent(Guid aggregateId, TEventPayload payload, Type aggregateType) =>
        new(aggregateId, payload, aggregateType);

    public List<CallHistory> GetCallHistoriesIncludesItself()
    {
        var histories = new List<CallHistory>();
        histories.AddRange(CallHistories);
        histories.Add(new CallHistory(Id, GetType().Name, string.Empty));
        return histories;
    }
}
