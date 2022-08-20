namespace Sekiban.EventSourcing.AggregateEvents
{
    [SekibanEventType]
    public record AggregateEvent<TEventPayload> : DocumentBase, IAggregateEvent
        where TEventPayload : IEventPayload
    {
        public TEventPayload Payload { get; init; } = default!;

        public string AggregateType { get; init; } = null!;

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
            TEventPayload eventPayload,
            bool isAggregateInitialEvent = false
        ) : base(
            aggregateId: aggregateId,
            partitionKey: PartitionKeyGenerator.ForAggregateEvent(aggregateId, aggregateType),
            documentType: DocumentType.AggregateEvent,
            documentTypeName: typeof(TEventPayload).Name
        )
        {
            Payload = eventPayload;
            AggregateType = aggregateType.Name;
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

        public static AggregateEvent<TEventPayload> CreatedEvent(Guid aggregateId, Type aggregateType, TEventPayload eventPayload) =>
            new(aggregateId, aggregateType, eventPayload, true);

        public static AggregateEvent<TEventPayload> ChangedEvent(Guid aggregateId, Type aggregateType, TEventPayload eventPayload) =>
            new(aggregateId, aggregateType, eventPayload);
    }
}
