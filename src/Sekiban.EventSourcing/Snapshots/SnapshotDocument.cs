using Newtonsoft.Json.Linq;
namespace Sekiban.EventSourcing.Snapshots;

public record SnapshotDocument : Document
{
    // jobjとしてはいるので変換が必要
    public dynamic? Snapshot { get; init; }
    public Guid AggregateId { get; init; }
    public Guid LastEventId { get; init; }
    public DateTime LastTimeStamp { get; set; }
    public SnapshotDocument() { }

    public SnapshotDocument(
        IPartitionKeyFactory partitionKeyFactory,
        string? aggregateTypeName,
        AggregateDtoBase dtoToSnapshot,
        Guid aggregateId,
        Guid lastEventId,
        DateTime lastTimeStamp,
        DateTime? timeStamp = null
    ) : base(
        DocumentType.AggregateSnapshot,
        partitionKeyFactory,
        aggregateTypeName)
    {
        Snapshot = dtoToSnapshot;
        AggregateId = aggregateId;
        LastEventId = lastEventId;
        LastTimeStamp = lastTimeStamp;
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
