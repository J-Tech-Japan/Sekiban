using Sekiban.Core.Document;
using Sekiban.Core.Partition;
using Sekiban.Core.Query.SingleProjections;
using Sekiban.Core.Shared;

namespace Sekiban.Core.Snapshot;

public record SnapshotDocument : DocumentBase, IDocument
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
        int savedVersion) : base(
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
    }

    public dynamic? Snapshot { get; init; }

    public Guid LastEventId { get; init; }

    public string LastSortableUniqueId { get; init; } = string.Empty;

    public int SavedVersion { get; init; }

    public T? ToState<T>() where T : IAggregateCommon
    {
        return SekibanJsonHelper.ConvertTo<T>(Snapshot);
    }
}
