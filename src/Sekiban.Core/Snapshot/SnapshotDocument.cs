using Sekiban.Core.Document;
using Sekiban.Core.Partition;
using Sekiban.Core.Query.SingleAggregate;
using Sekiban.Core.Shared;
namespace Sekiban.Core.Snapshot;

public record SnapshotDocument : DocumentBase, IDocument
{
    public dynamic? Snapshot { get; init; }

    public Guid LastEventId { get; init; }

    public string LastSortableUniqueId { get; init; } = string.Empty;

    public int SavedVersion { get; init; }

    public SnapshotDocument()
    {
    }

    public SnapshotDocument(
        Guid aggregateId,
        Type aggregateType,
        ISingleAggregate dtoToSnapshot,
        Guid lastEventId,
        string lastSortableUniqueId,
        int savedVersion) : base(
        aggregateId,
        PartitionKeyGenerator.ForAggregateSnapshot(aggregateId, aggregateType),
        DocumentType.AggregateSnapshot,
        aggregateType.Name ?? string.Empty)
    {
        Snapshot = dtoToSnapshot;
        AggregateId = aggregateId;
        LastEventId = lastEventId;
        LastSortableUniqueId = lastSortableUniqueId;
        SavedVersion = savedVersion;
    }

    public T? ToDto<T>() where T : ISingleAggregate
    {
        return SekibanJsonHelper.ConvertTo<T>(Snapshot);
    }
}
