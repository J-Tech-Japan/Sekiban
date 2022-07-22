using Newtonsoft.Json;
using System.Runtime.Serialization;
namespace Sekiban.EventSourcing.AggregateEvents;

[SekibanEventType]
public record AggregateEvent<TEventPayload> : Document, IAggregateEvent where TEventPayload : IEventPayload
{
    private int _version;
    public AggregateEvent(Guid aggregateId, TEventPayload payload, Type aggregateTypeObject, bool isAggregateInitialEvent = false) : base(
        DocumentType.AggregateEvent,
        null)
    {
        AggregateId = aggregateId;
        Payload = payload;
        AggregateType = aggregateTypeObject.Name;
        SetPartitionKey(new AggregateIdPartitionKeyFactory(aggregateId, aggregateTypeObject));
        IsAggregateInitialEvent = isAggregateInitialEvent;
    }
    [JsonConstructor]
    protected AggregateEvent() : base(DocumentType.AggregateEvent, null) { }
    [DataMember]
    public Guid AggregateId { get; init; }
    [DataMember]
    public string AggregateType { get; init; } = null!;

    /// <summary>
    ///     集約のスタートイベントの場合はtrueにする。
    /// </summary>
    [DataMember]
    public bool IsAggregateInitialEvent { get; protected set; }

    /// <summary>
    ///     集約のイベント適用後のバージョン
    /// </summary>
    [DataMember]
    public int Version
    {
        get => _version;
        init => _version = value;
    }
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

    public void SetVersion(int version) =>
        _version = version;
    public static AggregateEvent<TEventPayload> CreatedEvent<TAggregate>(Guid aggregateId, TEventPayload payload) where TAggregate : IAggregate =>
        new(aggregateId, payload, typeof(TEventPayload), true);
    public static AggregateEvent<TEventPayload> ChangedEvent<TAggregate>(Guid aggregateId, TEventPayload payload) where TAggregate : IAggregate =>
        new(aggregateId, payload, typeof(TEventPayload));

    public List<CallHistory> GetCallHistoriesIncludesItself()
    {
        var histories = new List<CallHistory>();
        histories.AddRange(CallHistories);
        histories.Add(new CallHistory(Id, GetType().Name, string.Empty));
        return histories;
    }
}
