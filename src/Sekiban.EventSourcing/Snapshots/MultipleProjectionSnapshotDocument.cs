using Sekiban.EventSourcing.Queries.SingleAggregates;
namespace Sekiban.EventSourcing.Snapshots;

public record MultipleProjectionSnapshotDocument : Document
{
    // jobjとしてはいるので変換が必要
    public string? SnapshotJson { get; init; }
    public Guid? BlobFileId { get; init; }
    public Guid AggregateId { get; init; }
    public Guid LastEventId { get; init; }
    public string LastSortableUniqueId { get; set; } = string.Empty;
    public int SavedVersion { get; set; }
    public MultipleProjectionSnapshotDocument() { }

    public MultipleProjectionSnapshotDocument(
        IPartitionKeyFactory partitionKeyFactory,
        string? aggregateTypeName,
        ISingleAggregate dtoToSnapshot,
        Guid aggregateId,
        Guid lastEventId,
        string lastSortableUniqueId,
        int savedVersion) : base(DocumentType.AggregateSnapshot, partitionKeyFactory, aggregateTypeName)
    {
        // Snapshot = dtoToSnapshot;
        AggregateId = aggregateId;
        LastEventId = lastEventId;
        LastSortableUniqueId = lastSortableUniqueId;
        SavedVersion = savedVersion;
    }
}
