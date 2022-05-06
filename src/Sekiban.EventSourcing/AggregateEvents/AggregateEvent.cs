namespace Sekiban.EventSourcing.AggregateEvents;

[SekibanEventType]
public record AggregateEvent : Document, IAggregateEvent, ICallHistories
{
    private int _version;

    public Guid AggregateId { get; init; }
    public string AggregateType { get; init; } = null!;

    /// <summary>
    ///     集約のスタートイベントの場合はtrueにする。
    /// </summary>
    public bool IsAggregateInitialEvent { get; protected set; }

    /// <summary>
    ///     集約のイベント適用後のバージョン
    /// </summary>
    public int Version
    {
        get => _version;
        init => _version = value;
    }

    public AggregateEvent() { }

    public AggregateEvent(Guid aggregateId, Type aggregateType, bool isAggregateInitialEvent = false) : base(DocumentType.AggregateEvent, null)
    {
        AggregateId = aggregateId;
        AggregateType = aggregateType.Name;
        SetPartitionKey(new AggregateIdPartitionKeyFactory(aggregateId, aggregateType));
        IsAggregateInitialEvent = isAggregateInitialEvent;
    }

    public List<CallHistory> CallHistories { get; init; } = new();

    public void SetVersion(int version) => _version = version;

    public List<CallHistory> GetCallHistoriesIncludesItself()
    {
        var histories = new List<CallHistory>();
        histories.AddRange(CallHistories);
        histories.Add(new CallHistory(Id, GetType().Name, string.Empty));
        return histories;
    }
}
