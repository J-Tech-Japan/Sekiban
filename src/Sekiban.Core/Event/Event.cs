using Sekiban.Core.Document;
using Sekiban.Core.History;
using Sekiban.Core.Partition;
namespace Sekiban.Core.Event;

[SekibanEventType]
public record Event<TEventPayload> : DocumentBase, IEvent where TEventPayload : IEventPayload
{

    public Event()
    {
    }

    public Event(Guid aggregateId, Type aggregateType, TEventPayload eventPayload, bool isAggregateInitialEvent = false) : base(
        aggregateId,
        PartitionKeyGenerator.ForAggregateEvent(aggregateId, aggregateType),
        DocumentType.AggregateEvent,
        typeof(TEventPayload).Name)
    {
        Payload = eventPayload;
        AggregateType = aggregateType.Name;
        IsAggregateInitialEvent = isAggregateInitialEvent;
    }
    public TEventPayload Payload { get; init; } = default!;

    public string AggregateType { get; init; } = null!;

    /// <summary>
    ///     集約のスタートイベントの場合はtrueにする。
    /// </summary>
    public bool IsAggregateInitialEvent { get; init; }

    /// <summary>
    ///     集約のイベント適用後のバージョン
    /// </summary>
    public int Version { get; init; }

    public List<CallHistory> CallHistories { get; init; } = new();

    public dynamic GetComparableObject(IEvent original, bool copyVersion = true) => this with
    {
        Version = copyVersion ? original.Version : Version,
        SortableUniqueId = original.SortableUniqueId,
        CallHistories = original.CallHistories,
        Id = original.Id,
        TimeStamp = original.TimeStamp
    };

    public IEventPayload GetPayload() => Payload;
    public T? GetPayload<T>() where T : class, IEventPayload => Payload as T;

    public List<CallHistory> GetCallHistoriesIncludesItself()
    {
        var histories = new List<CallHistory>();
        histories.AddRange(CallHistories);
        histories.Add(new CallHistory(Id, GetType().Name, string.Empty));
        return histories;
    }

    public static Event<TEventPayload> CreatedEvent(Guid aggregateId, Type aggregateType, TEventPayload eventPayload) =>
        new Event<TEventPayload>(aggregateId, aggregateType, eventPayload, true);

    public static Event<TEventPayload> ChangedEvent(Guid aggregateId, Type aggregateType, TEventPayload eventPayload) =>
        new Event<TEventPayload>(aggregateId, aggregateType, eventPayload);
}
