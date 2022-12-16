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
        IAggregateCommon stateToSnapshot,
        Guid lastEventId,
        string lastSortableUniqueId,
        int savedVersion,
        string payloadVersionIdentifier) : base(
        aggregateId,
        PartitionKeyGenerator.ForAggregateSnapshot(aggregateId, aggregateType),
        DocumentType.AggregateSnapshot,
        aggregateType.Name ?? string.Empty)
    {
        Snapshot = stateToSnapshot;
        AggregateId = aggregateId;
        LastEventId = lastEventId;
        LastSortableUniqueId = lastSortableUniqueId;
        SavedVersion = savedVersion;
        PayloadVersionIdentifier = payloadVersionIdentifier;
    }

    public dynamic? Snapshot { get; init; }

    public Guid LastEventId { get; init; }

    public string LastSortableUniqueId { get; init; } = string.Empty;

    public int SavedVersion { get; init; }

    public string PayloadVersionIdentifier { get; init; } = string.Empty;

    public T? ToState<T>() where T : IAggregateCommon => SekibanJsonHelper.ConvertTo<T>(Snapshot);
}
