using Sekiban.Core.Document;
using Sekiban.Core.History;
using Sekiban.Core.Partition;
namespace Sekiban.Core.Event;

public record Event<TEventPayload> : DocumentBase, IEvent where TEventPayload : IEventPayloadCommon
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

    public List<CallHistory> GetCallHistoriesIncludesItself()
    {
        var histories = new List<CallHistory>();
        histories.AddRange(CallHistories);
        histories.Add(new CallHistory(Id, GetType().Name, string.Empty));
        return histories;
    }

    public static Event<TEventPayload> GenerateEvent(Guid aggregateId, Type aggregateType, TEventPayload eventPayload) =>
        new(aggregateId, aggregateType, eventPayload);
}
