using Sekiban.EventSourcing.Queries.SingleAggregates;

namespace Sekiban.EventSourcing.Snapshots;

public record SnapshotDocument : DocumentBase, IDocument
{
    public dynamic? Snapshot { get; init; }

    public Guid LastEventId { get; init; }

    public string LastSortableUniqueId { get; init; } = string.Empty;

    public int SavedVersion { get; init; }

    public SnapshotDocument()
    { }

    public SnapshotDocument(
        Guid aggregateId,
        Type aggregateType,
        ISingleAggregate dtoToSnapshot,
        Guid lastEventId,
        string lastSortableUniqueId,
        int savedVersion
    ) : base(
        aggregateId: aggregateId,
        partitionKey: PartitionKeyGenerator.ForAggregateSnapshot(aggregateId, aggregateType),
        documentType: DocumentType.AggregateSnapshot,
        documentTypeName: aggregateType.Name ?? string.Empty
    )
    {
        Snapshot = dtoToSnapshot;
        AggregateId = aggregateId;
        LastEventId = lastEventId;
        LastSortableUniqueId = lastSortableUniqueId;
        SavedVersion = savedVersion;
    }

    public T? ToDto<T>() where T : ISingleAggregate
    {
        return Shared.SekibanJsonHelper.ConvertTo<T>(Snapshot);
    }
}
