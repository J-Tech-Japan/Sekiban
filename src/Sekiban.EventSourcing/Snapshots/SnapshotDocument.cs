using Sekiban.EventSourcing.Queries.SingleAggregates;

namespace Sekiban.EventSourcing.Snapshots;

public record SnapshotDocument : IDocument
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    public string PartitionKey { get; init; } = default!;

    public DocumentType DocumentType { get; init; }

    public string DocumentTypeName { get; init; } = null!;

    public DateTime TimeStamp { get; init; }

    public string SortableUniqueId { get; init; } = string.Empty;

    public dynamic? Snapshot { get; init; }
    public Guid AggregateId { get; init; }
    public Guid LastEventId { get; init; }
    public string LastSortableUniqueId { get; init; } = string.Empty;
    public int SavedVersion { get; init; }

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
        return Shared.SekibanJsonHelper.ConvertTo<T>(Snapshot);
    }
}
