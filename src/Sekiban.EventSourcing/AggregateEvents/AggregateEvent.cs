namespace Sekiban.EventSourcing.AggregateEvents;

[SekibanEventType]
public record AggregateEvent<TEventPayload> : DocumentBase, IAggregateEvent
    where TEventPayload : IEventPayload
{
    public Guid AggregateId { get; init; }

    public string AggregateType { get; init; } = null!;

    public TEventPayload Payload { get; init; } = default!;

    /// <summary>
    /// 集約のスタートイベントの場合はtrueにする。
    /// </summary>
    public bool IsAggregateInitialEvent { get; init; }

    /// <summary>
    /// 集約のイベント適用後のバージョン
    /// </summary>
    public int Version { get; init; }

    public List<CallHistory> CallHistories { get; init; } = new();

    public AggregateEvent()
    { }

    public AggregateEvent(
        Guid aggregateId,
        Type aggregateType,
        TEventPayload payload,
        bool isAggregateInitialEvent = false
    ) : base(
        partitionKey: PartitionKeyCreator.ForAggregateEvent(aggregateId, aggregateType),
        documentType: DocumentType.AggregateEvent,
        documentTypeName: typeof(TEventPayload).Name
    )
    {
        AggregateId = aggregateId;
        AggregateType = aggregateType.Name;
        Payload = payload;
        IsAggregateInitialEvent = isAggregateInitialEvent;
    }

    public dynamic GetComparableObject(IAggregateEvent original, bool copyVersion = true) =>
        this with
        {
            Version = copyVersion ? original.Version : Version,
            SortableUniqueId = original.SortableUniqueId,
            CallHistories = original.CallHistories,
            Id = original.Id,
            TimeStamp = original.TimeStamp
        };

    public IEventPayload GetPayload() =>
        Payload;

    public List<CallHistory> GetCallHistoriesIncludesItself()
    {
        var histories = new List<CallHistory>();
        histories.AddRange(CallHistories);
        histories.Add(new CallHistory(Id, GetType().Name, string.Empty));
        return histories;
    }

    public static AggregateEvent<TEventPayload> CreatedEvent(Guid aggregateId, Type aggregateType, TEventPayload payload) =>
        new(aggregateId, aggregateType, payload, true);

    public static AggregateEvent<TEventPayload> ChangedEvent(Guid aggregateId, Type aggregateType, TEventPayload payload) =>
        new(aggregateId, aggregateType, payload);
}
