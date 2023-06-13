using Sekiban.Core.Documents;
using Sekiban.Core.Documents.ValueObjects;
using Sekiban.Core.Partition;
using Sekiban.Core.Query.MultiProjections;
using Sekiban.Core.Shared;
namespace Sekiban.Core.Snapshot;

public record MultiProjectionSnapshotDocument : IDocument
{
    public const string AllRootPartitionKey = "all";
    public Guid LastEventId { get; init; }

    public string LastSortableUniqueId { get; init; } = string.Empty;

    public int SavedVersion { get; init; }

    public string PayloadVersionIdentifier { get; init; } = string.Empty;
    public MultiProjectionSnapshotDocument() { }

    public MultiProjectionSnapshotDocument(Type projectionType, Guid id, IMultiProjectionCommon projection, string rootPartitionKey)
    {
        Id = id;
        RootPartitionKey = rootPartitionKey ?? AllRootPartitionKey;
        DocumentTypeName = DocumentTypeNameFromProjectionType(projectionType);
        DocumentType = DocumentType.MultiProjectionSnapshot;
        PartitionKey = PartitionKeyGenerator.ForMultiProjectionSnapshot(projectionType, RootPartitionKey);
        TimeStamp = SekibanDateProducer.GetRegistered().UtcNow;
        SortableUniqueId = SortableUniqueIdValue.Generate(TimeStamp, Id);
        LastEventId = projection.LastEventId;
        LastSortableUniqueId = projection.LastSortableUniqueId;
        SavedVersion = projection.Version;
        PayloadVersionIdentifier = projection.GetPayloadVersionIdentifier();
        AggregateType = DocumentTypeName;
    }
    [JsonPropertyName("id")]
    public Guid Id { get; init; } = Guid.NewGuid();
    public string PartitionKey { get; init; } = default!;
    public DocumentType DocumentType { get; init; }
    public string DocumentTypeName { get; init; } = default!;
    public DateTime TimeStamp { get; init; }
    public string SortableUniqueId { get; init; } = default!;
    public string AggregateType { get; init; } = string.Empty;
    public string RootPartitionKey { get; init; } = string.Empty;
    public SortableUniqueIdValue GetSortableUniqueId() => SortableUniqueId;
    public static string DocumentTypeNameFromProjectionType(Type projectionType)
    {
        if (projectionType.IsGenericType && projectionType.GetGenericTypeDefinition() == typeof(SingleProjectionListState<>))
        {
            var stateType = projectionType.GenericTypeArguments[0];
            if (stateType.IsGenericType)
            {
                return stateType.GenericTypeArguments[0].Name;
            }
        }
        return projectionType.Name;
    }
}
