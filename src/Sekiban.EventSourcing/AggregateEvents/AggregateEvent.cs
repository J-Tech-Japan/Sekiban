using Newtonsoft.Json;
using System.Runtime.Serialization;
namespace Sekiban.EventSourcing.AggregateEvents;

[SekibanEventType]
public record AggregateEvent : Document, IAggregateEvent, ICallHistories
{
    private int _version;
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
    public AggregateEvent(Guid aggregateId, Type aggregateTypeObject, bool isAggregateInitialEvent = false) : base(DocumentType.AggregateEvent, null)
    {
        AggregateId = aggregateId;
        AggregateType = aggregateTypeObject.Name;
        SetPartitionKey(new AggregateIdPartitionKeyFactory(aggregateId, aggregateTypeObject));
        IsAggregateInitialEvent = isAggregateInitialEvent;
    }
    [JsonConstructor]
    protected AggregateEvent() : base(DocumentType.AggregateEvent, null) { }

    [DataMember]
    public List<CallHistory> CallHistories { get; init; } = new();

    public dynamic GetComparableObject(AggregateEvent original, bool copyVersion = true) =>
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

    public List<CallHistory> GetCallHistoriesIncludesItself()
    {
        var histories = new List<CallHistory>();
        histories.AddRange(CallHistories);
        histories.Add(new CallHistory(Id, GetType().Name, string.Empty));
        return histories;
    }
}
