namespace Sekiban.EventSourcing.Queries;

public class SingleAggregateList<T>
    where T : ISingleAggregate
{
    private List<SnapshotListIndex>? _mergedSnapshotIds;
    public List<T> List { get; set; } = new();
    public Guid? LastEventId { get; set; } = null;
    public string LastSortableUniqueId { get; set; } = string.Empty;
    public SnapshotListDocument? ProjectedSnapshot { get; set; } = null;
    public List<SnapshotListChunkDocument> ProjectedSnapshotChunks { get; set; } = new();

    public List<SnapshotListIndex> MergedSnapshotIds
    {
        get
        {
            if (_mergedSnapshotIds != null)
            {
                return _mergedSnapshotIds;
            }
            _mergedSnapshotIds = new List<SnapshotListIndex>();
            _mergedSnapshotIds.AddRange(
                ProjectedSnapshot?.SnapshotIds ?? new List<SnapshotListIndex>());
            foreach (var c in ProjectedSnapshotChunks)
            {
                _mergedSnapshotIds.AddRange(c.SnapshotIds);
            }
            return _mergedSnapshotIds;
        }
    }

    public static string UniqueKey() =>
        $"AggregateList-{typeof(T).Name}";
}
