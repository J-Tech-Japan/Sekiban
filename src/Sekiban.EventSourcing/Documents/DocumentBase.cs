namespace Sekiban.EventSourcing.Documents;

public abstract record class DocumentBase : IDocument
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    public Guid AggregateId { get; init; }

    public string PartitionKey { get; init; } = default!;

    public DocumentType DocumentType { get; init; }

    public string DocumentTypeName { get; init; } = default!;

    public DateTime TimeStamp { get; init; }

    public string SortableUniqueId { get; init; } = default!;

    public DocumentBase()
    { }

    public DocumentBase(Guid aggregateId, string partitionKey, DocumentType documentType, string documentTypeName)
    {
        Id = Guid.NewGuid();
        AggregateId = aggregateId;
        PartitionKey = partitionKey;
        DocumentType = documentType;
        DocumentTypeName = documentTypeName;
        TimeStamp = DateTime.UtcNow;
        SortableUniqueId = SortableUniqueIdGenerator.Generate(TimeStamp, Id);
    }
}