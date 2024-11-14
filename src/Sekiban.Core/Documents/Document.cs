using Sekiban.Core.Documents.ValueObjects;
using Sekiban.Core.Shared;
namespace Sekiban.Core.Documents;

/// <summary>
///     General Document abstract.
///     it will be inherited by Events, CommandDocument etc.
/// </summary>
public abstract record Document : IAggregateDocument
{
    // ReSharper disable once PublicConstructorInAbstractClass
    public Document()
    {
    }

    // ReSharper disable once PublicConstructorInAbstractClass
    public Document(
        Guid aggregateId,
        string partitionKey,
        DocumentType documentType,
        string documentTypeName,
        string aggregateType,
        string rootPartitionKey)
    {
#if NET9_0_OR_GREATER
        Id = Guid.CreateVersion7();
#else
        Id = Guid.NewGuid();
#endif
        AggregateId = aggregateId;
        PartitionKey = partitionKey;
        DocumentType = documentType;
        DocumentTypeName = documentTypeName;
        TimeStamp = SekibanDateProducer.GetRegistered().UtcNow;
        SortableUniqueId = SortableUniqueIdValue.Generate(TimeStamp, Id);
        AggregateType = aggregateType;
        RootPartitionKey = rootPartitionKey;
    }

    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    public Guid AggregateId { get; init; }

    public string PartitionKey { get; init; } = default!;

    public DocumentType DocumentType { get; init; }

    public string DocumentTypeName { get; init; } = default!;

    public DateTime TimeStamp { get; init; }

    public string SortableUniqueId { get; init; } = default!;
    public string AggregateType { get; init; } = string.Empty;
    public string RootPartitionKey { get; init; } = string.Empty;
    public SortableUniqueIdValue GetSortableUniqueId() => SortableUniqueId;
}
