using Sekiban.Core.Events;
using Sekiban.Core.Shared;

namespace Sekiban.Infrastructure.IndexedDb.Databases;

public record DbEvent
{
    public string Id { get; init; } = string.Empty;
    public string Payload { get; init; } = string.Empty;
    public int Version { get; init; }
    public string CallHistories { get; init; } = string.Empty;
    public string AggregateId { get; init; } = string.Empty;
    public string PartitionKey { get; init; } = string.Empty;
    public string DocumentType { get; init; } = string.Empty;
    public string DocumentTypeName { get; init; } = string.Empty;
    public string TimeStamp { get; init; } = string.Empty;
    public string SortableUniqueId { get; init; } = string.Empty;
    public string AggregateType { get; init; } = string.Empty;
    public string RootPartitionKey { get; init; } = string.Empty;

    public static DbEvent FromEvent(IEvent ev) =>
        new()
        {
            Id = ev.Id.ToString(),
            Payload = SekibanJsonHelper.Serialize(ev.GetPayload()) ?? string.Empty,
            Version = ev.Version,
            CallHistories = SekibanJsonHelper.Serialize(ev.CallHistories) ?? string.Empty,
            AggregateId = ev.AggregateId.ToString(),
            PartitionKey = ev.PartitionKey,
            DocumentType = ev.DocumentType.ToString(),
            DocumentTypeName = ev.DocumentTypeName,
            TimeStamp = DateTimeConverter.ToString(ev.TimeStamp),
            SortableUniqueId = ev.SortableUniqueId,
            AggregateType = ev.AggregateType,
            RootPartitionKey = ev.RootPartitionKey,
        };
}
