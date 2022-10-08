using Sekiban.EventSourcing.Documents.ValueObjects;
using Sekiban.EventSourcing.Queries.SingleAggregates;
using Sekiban.EventSourcing.Shared;
namespace Sekiban.EventSourcing.Snapshots
{
    public record MultipleProjectionSnapshotDocument : IDocument
    {

        // jobjとしてはいるので変換が必要
        public string? SnapshotJson { get; init; }
        public Guid? BlobFileId { get; init; }
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
            TimeStamp = SekibanDateProducer.GetRegistered().UtcNow;
            SortableUniqueId = SortableUniqueIdValue.Generate(TimeStamp, Id);
            PartitionKey = partitionKeyFactory.GetPartitionKey(DocumentType);
            // Snapshot = dtoToSnapshot;
            AggregateId = aggregateId;
            LastEventId = lastEventId;
            LastSortableUniqueId = lastSortableUniqueId;
            SavedVersion = savedVersion;
        }
        [JsonPropertyName("id")]
        public Guid Id { get; init; }

        public string PartitionKey { get; init; }

        public DocumentType DocumentType { get; init; }

        public string DocumentTypeName { get; init; } = null!;

        public DateTime TimeStamp { get; init; }

        public string SortableUniqueId { get; init; } = string.Empty;
        public SortableUniqueIdValue GetSortableUniqueId() =>
            SortableUniqueId;
        public Guid AggregateId { get; init; }
    }
}
