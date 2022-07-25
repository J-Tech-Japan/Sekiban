using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Sekiban.EventSourcing.Queries.SingleAggregates;
using System.Runtime.Serialization;
namespace Sekiban.EventSourcing.Snapshots;

public record SnapshotDocument : IDocument
{
    [JsonProperty("id")]
    [DataMember]
    public Guid Id { get; init; }
    [DataMember]
    public string PartitionKey { get; init; } = default!;

    [DataMember]
    public DocumentType DocumentType { get; init; }
    [DataMember]
    public string DocumentTypeName { get; init; } = null!;
    [DataMember]
    public DateTime TimeStamp { get; init; }
    [DataMember]
    public string SortableUniqueId { get; init; } = string.Empty;

    // jobjとしてはいるので変換が必要
    public dynamic? Snapshot { get; init; }
    public Guid AggregateId { get; init; }
    public Guid LastEventId { get; init; }
    public string LastSortableUniqueId { get; init; } = string.Empty;
    public int SavedVersion { get; init; }

    [JsonConstructor]
    public SnapshotDocument()
    { }

    public SnapshotDocument(
        IPartitionKeyFactory partitionKeyFactory,
        string? aggregateTypeName,
        ISingleAggregate dtoToSnapshot,
        Guid aggregateId,
        Guid lastEventId,
        string lastSortableUniqueId,
        int savedVersion)
    {
        Id = Guid.NewGuid();
        DocumentType = DocumentType.AggregateSnapshot;
        DocumentTypeName = aggregateTypeName ?? string.Empty;
        TimeStamp = DateTime.UtcNow;
        SortableUniqueId = SortableUniqueIdGenerator.Generate(TimeStamp, Id);
        PartitionKey = partitionKeyFactory.GetPartitionKey(DocumentType);
        Snapshot = dtoToSnapshot;
        AggregateId = aggregateId;
        LastEventId = lastEventId;
        LastSortableUniqueId = lastSortableUniqueId;
        SavedVersion = savedVersion;
    }

    public T? ToDto<T>() where T : ISingleAggregate
    {
        if (Snapshot is not JObject jobj)
        {
            return default;
        }
        return jobj.ToObject<T>();
    }
}
