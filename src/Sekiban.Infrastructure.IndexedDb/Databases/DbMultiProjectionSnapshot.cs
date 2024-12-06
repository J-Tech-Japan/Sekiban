using Sekiban.Core.Aggregate;
using Sekiban.Core.Documents;
using Sekiban.Core.Snapshot;

namespace Sekiban.Infrastructure.IndexedDb;

public record DbMultiProjectionSnapshot
{
    public string Id { get; init; } = string.Empty;
    public string AggregateContainerGroup { get; init; } = string.Empty;
    public string PartitionKey { get; init; } = string.Empty;
    public string DocumentType { get; init; } = string.Empty;
    public string DocumentTypeName { get; init; } = string.Empty;
    public string TimeStamp { get; init; } = string.Empty;
    public string SortableUniqueId { get; init; } = string.Empty;
    public string AggregateType { get; init; } = string.Empty;
    public string RootPartitionKey { get; init; } = string.Empty;
    public string LastEventId { get; init; } = string.Empty;
    public string LastSortableUniqueId { get; init; } = string.Empty;
    public int SavedVersion { get; init; }
    public string PayloadVersionIdentifier { get; init; } = string.Empty;

    public static DbMultiProjectionSnapshot FromSnapshot(MultiProjectionSnapshotDocument snapshot, AggregateContainerGroup aggregateContainerGroup) =>
        new()
        {
            AggregateContainerGroup = aggregateContainerGroup.ToString(),
            Id = snapshot.Id.ToString(),
            PartitionKey = snapshot.PartitionKey,
            DocumentType = snapshot.DocumentType.ToString(),
            DocumentTypeName = snapshot.DocumentTypeName,
            TimeStamp = DateTimeConverter.ToString(snapshot.TimeStamp),
            SortableUniqueId = snapshot.SortableUniqueId,
            AggregateType = snapshot.AggregateType,
            RootPartitionKey = snapshot.RootPartitionKey,
            LastEventId = snapshot.LastEventId.ToString(),
            LastSortableUniqueId = snapshot.LastSortableUniqueId,
            SavedVersion = snapshot.SavedVersion,
            PayloadVersionIdentifier = snapshot.PayloadVersionIdentifier
        };

    public MultiProjectionSnapshotDocument ToSnapshot() =>
        new()
        {
            Id = new Guid(Id),
            PartitionKey = PartitionKey,
            DocumentType = Enum.Parse<DocumentType>(DocumentType),
            DocumentTypeName = DocumentTypeName,
            TimeStamp = DateTimeConverter.ToDateTime(TimeStamp),
            SortableUniqueId = SortableUniqueId,
            AggregateType = AggregateType,
            RootPartitionKey = RootPartitionKey,
            LastEventId = new Guid(LastEventId),
            LastSortableUniqueId = LastSortableUniqueId,
            SavedVersion = SavedVersion,
            PayloadVersionIdentifier = PayloadVersionIdentifier,
        };
}
