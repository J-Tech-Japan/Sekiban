namespace Sekiban.EventSourcing.AggregateEvents;

[SekibanEventType]
public record AggregateEvent<TEventPayload> : IAggregateEvent where TEventPayload : IEventPayload
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    public string PartitionKey { get; init; } = default!;

    public DocumentType DocumentType { get; init; }

    public string DocumentTypeName { get; init; } = null!;

    public DateTime TimeStamp { get; init; }

    public string SortableUniqueId { get; init; } = string.Empty;

    public Guid AggregateId { get; init; }

    public string AggregateType { get; init; } = null!;

    public TEventPayload Payload { get; init; } = default!;

    /// <summary>
    ///     集約のスタートイベントの場合はtrueにする。
    /// </summary>
    public bool IsAggregateInitialEvent { get; init; }

    /// <summary>
    ///     集約のイベント適用後のバージョン
    /// </summary>
    public int Version { get; init; }

    public List<CallHistory> CallHistories { get; init; } = new();

    [JsonConstructor]
    protected AggregateEvent()
    { }

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
