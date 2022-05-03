using Newtonsoft.Json.Linq;
namespace Sekiban.EventSourcing.Snapshots;

public record SnapshotDocument : Document
{
    // jobjとしてはいるので変換が必要
    public dynamic? Snapshot { get; init; }
    public Guid AggregateId { get; init; }
    public Guid LastEventId { get; init; }
    public string LastSortableUniqueId { get; set; } = string.Empty;
    public SnapshotDocument() { }

    public SnapshotDocument(
        IPartitionKeyFactory partitionKeyFactory,
        string? aggregateTypeName,
        AggregateDtoBase dtoToSnapshot,
        Guid aggregateId,
        Guid lastEventId,
        string lastSortableUniqueId,
        DateTime? timeStamp = null
    ) : base(
        DocumentType.AggregateSnapshot,
        partitionKeyFactory,
        aggregateTypeName)
    {
        Snapshot = dtoToSnapshot;
        AggregateId = aggregateId;
        LastEventId = lastEventId;
        LastSortableUniqueId = lastSortableUniqueId;
    }

    public T? ToDto<T>()
        where T : AggregateDtoBase, new()
    {
        if (Snapshot is not JObject jobj)
        {
            return null;
        }
        return jobj.ToObject<T>();
    }
}
