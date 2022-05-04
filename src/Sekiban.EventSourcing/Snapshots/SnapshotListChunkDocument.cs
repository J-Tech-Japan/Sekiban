namespace Sekiban.EventSourcing.Snapshots;

public record SnapshotListChunkDocument : Document
{
    public List<SnapshotListIndex> SnapshotIds { get; set; }
    public int ChunkIndex { get; set; }
    public int ItemCount { get; set; }

    public SnapshotListChunkDocument(
        List<SnapshotListIndex> snapshotIds,
        int chunkIndex,
        IPartitionKeyFactory partitionKeyFactory,
        string aggregateTypeName) : base(
        DocumentType.SnapshotListChunk,
        partitionKeyFactory,
        aggregateTypeName)
    {
        SnapshotIds = snapshotIds;
        ItemCount = snapshotIds.Count;
        ChunkIndex = chunkIndex;
    }
}
