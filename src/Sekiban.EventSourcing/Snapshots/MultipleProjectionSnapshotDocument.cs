using Newtonsoft.Json;
using Sekiban.EventSourcing.Queries.SingleAggregates;
using System.Runtime.Serialization;
namespace Sekiban.EventSourcing.Snapshots;

public record MultipleProjectionSnapshotDocument : IDocument
{
    // jobjとしてはいるので変換が必要
    public string? SnapshotJson { get; init; }
    public Guid? BlobFileId { get; init; }
    public Guid AggregateId { get; init; }
    public Guid LastEventId { get; init; }
    public string LastSortableUniqueId { get; set; } = string.Empty;
    public int SavedVersion { get; set; }
    public MultipleProjectionSnapshotDocument(
        IPartitionKeyFactory partitionKeyFactory,
        string? aggregateTypeName,
        ISingleAggregate dtoToSnapshot,
        Guid aggregateId,
        Guid lastEventId,
        string lastSortableUniqueId,
        int savedVersion)
    {
        Id = Guid.NewGuid();
        DocumentType = DocumentType.MultipleAggregateSnapshot;
        DocumentTypeName = aggregateTypeName ?? string.Empty;
        TimeStamp = DateTime.UtcNow;
        SortableUniqueId = SortableUniqueIdGenerator.Generate(TimeStamp, Id);
        PartitionKey = partitionKeyFactory.GetPartitionKey(DocumentType);
        // Snapshot = dtoToSnapshot;
        AggregateId = aggregateId;
        LastEventId = lastEventId;
        LastSortableUniqueId = lastSortableUniqueId;
        SavedVersion = savedVersion;
    }
    [JsonProperty("id")]
    [DataMember]
    public Guid Id { get; init; }
    [DataMember]
    public string PartitionKey { get; init; }

    [DataMember]
    public DocumentType DocumentType { get; init; }
    [DataMember]
    public string DocumentTypeName { get; init; } = null!;
    [DataMember]
    public DateTime TimeStamp { get; init; }
    [DataMember]
    public string SortableUniqueId { get; init; } = string.Empty;
}
