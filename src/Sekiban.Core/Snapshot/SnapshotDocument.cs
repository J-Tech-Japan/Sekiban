using Sekiban.Core.Document;
using Sekiban.Core.Partition;
using Sekiban.Core.Query.SingleProjections;
using Sekiban.Core.Shared;
namespace Sekiban.Core.Snapshot;

public record SnapshotDocument : Document.Document, IDocument
{
    public SnapshotDocument()
    {
    }

    public SnapshotDocument(
        Guid aggregateId,
        Type aggregateType,
        Type payloadType,
        IAggregateCommon stateToSnapshot,
        Guid lastEventId,
        string lastSortableUniqueId,
        int savedVersion,
        string payloadVersionIdentifier) : base(
        aggregateId,
        PartitionKeyGenerator.ForAggregateSnapshot(aggregateId, aggregateType, payloadType),
        DocumentType.AggregateSnapshot,
        payloadType.Name ?? string.Empty)
    {
        Snapshot = stateToSnapshot;
        AggregateTypeName = aggregateType.Name;
        AggregateId = aggregateId;
        LastEventId = lastEventId;
        LastSortableUniqueId = lastSortableUniqueId;
        SavedVersion = savedVersion;
        PayloadVersionIdentifier = payloadVersionIdentifier;
    }

    public dynamic? Snapshot { get; init; }

    public string AggregateTypeName { get; init; } = string.Empty;

    public Guid LastEventId { get; init; }

    public string LastSortableUniqueId { get; init; } = string.Empty;

    public int SavedVersion { get; init; }

    public string PayloadVersionIdentifier { get; init; } = string.Empty;

    public T? ToState<T>() where T : IAggregateCommon => SekibanJsonHelper.ConvertTo<T>(Snapshot);
}
