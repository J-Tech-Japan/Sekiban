namespace Sekiban.EventSourcing.Snapshots;

public record SnapshotListDocument : Document
{
    private const int LIST_LIMIT_SIZE = 3;

    public List<SnapshotListIndex> SnapshotIds { get; set; }
    public List<Guid> SnapshotListChunkIds { get; set; } = new();
    public Guid? ProjectedSourceSnapshotListId { get; set; }
    public string? TargetPartitionKey { get; set; }
    public Guid LastEventId { get; set; }
    public int TotalItemCount { get; set; }
    public int TotalChunksCount { get; set; }

    public SnapshotListDocument(
        List<SnapshotListIndex> snapshotIds,
        Guid? projectedSourceSnapshotListId,
        Guid lastEventId,
        IPartitionKeyFactory partitionKeyFactory,
        string aggregateTypeName) : base(DocumentType.SnapshotList, partitionKeyFactory, aggregateTypeName)
    {
        SnapshotIds = snapshotIds;
        ProjectedSourceSnapshotListId = projectedSourceSnapshotListId;
        LastEventId = lastEventId;
        TotalItemCount = snapshotIds.Count;
    }

    public static (SnapshotListDocument?, List<SnapshotListChunkDocument>?) CreateSnapshotListDocument(
        List<SnapshotListIndex> snapshotIds,
        Guid? projectedSourceSnapshotListId,
        Guid lastEventId,
        IPartitionKeyFactory partitionKeyFactory,
        string aggregateTypeName)
    {
        var split = new List<List<SnapshotListIndex>>();
        while (true)
        {
            switch (snapshotIds.Count)
            {
                case >= LIST_LIMIT_SIZE:
                    split.Add(snapshotIds.GetRange(0, LIST_LIMIT_SIZE));
                    snapshotIds.RemoveRange(0, LIST_LIMIT_SIZE);
                    continue;
                case > 0:
                    split.Add(snapshotIds);
                    break;
            }
            break;
        }
        if (split.Count == 0) { return (null, null); }
        SnapshotListDocument? snapshotList = null;
        List<SnapshotListChunkDocument> snapshotListChunks = new();
        foreach (var (s, i) in split.Select((value, i) => (value, i)))
        {
            if (i == 0)
            {
                snapshotList = new SnapshotListDocument(s, projectedSourceSnapshotListId, lastEventId, partitionKeyFactory, aggregateTypeName);
            } else
            {
                var chunk = new SnapshotListChunkDocument(s, i, partitionKeyFactory, aggregateTypeName);
                if (snapshotList != null)
                {
                    snapshotList.SnapshotListChunkIds.Add(chunk.Id);
                    snapshotList.TotalChunksCount++;
                    snapshotList.TotalItemCount += s.Count;
                }
                snapshotListChunks.Add(chunk);
            }
        }
        return (snapshotList, snapshotListChunks);
    }
}
