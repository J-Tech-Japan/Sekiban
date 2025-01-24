using System.Text.Json;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Documents;
using Sekiban.Core.Shared;
using Sekiban.Core.Snapshot;

namespace Sekiban.Infrastructure.IndexedDb;

public record DbSingleProjectionSnapshot
{
    public string Id { get; init; } = string.Empty;
    public string AggregateContainerGroup { get; init; } = string.Empty;
    public string? Snapshot { get; init; }
    public string LastEventId { get; init; } = string.Empty;
    public string LastSortableUniqueId { get; init; } = string.Empty;
    public int SavedVersion { get; init; }

    public string PayloadVersionIdentifier { get; init; } = string.Empty;
    public string AggregateId { get; init; } = string.Empty;
    public string PartitionKey { get; init; } = string.Empty;
    public string DocumentType { get; init; } = string.Empty;
    public string DocumentTypeName { get; init; } = string.Empty;
    public string TimeStamp { get; init; } = string.Empty;
    public string SortableUniqueId { get; init; } = string.Empty;
    public string AggregateType { get; init; } = string.Empty;
    public string RootPartitionKey { get; init; } = string.Empty;


    public static DbSingleProjectionSnapshot FromSnapshot(SnapshotDocument snapshot, AggregateContainerGroup aggregateContainerGroup) =>
        new()
        {
            Id = snapshot.Id.ToString(),
            AggregateContainerGroup = aggregateContainerGroup.ToString(),
            Snapshot = snapshot.Snapshot is null ? null : SekibanJsonHelper.Serialize(snapshot.Snapshot),
            LastEventId = snapshot.LastEventId.ToString(),
            LastSortableUniqueId = snapshot.LastSortableUniqueId.ToString(),
            SavedVersion = snapshot.SavedVersion,
            PayloadVersionIdentifier = snapshot.PayloadVersionIdentifier,
            AggregateId = snapshot.AggregateId.ToString(),
            PartitionKey = snapshot.PartitionKey,
            DocumentType = snapshot.DocumentType.ToString(),
            DocumentTypeName = snapshot.DocumentTypeName,
            TimeStamp = DateTimeConverter.ToString(snapshot.TimeStamp),
            SortableUniqueId = snapshot.SortableUniqueId,
            AggregateType = snapshot.AggregateType,
            RootPartitionKey = snapshot.RootPartitionKey,
        };

    public SnapshotDocument ToSnapshot()
    {
        var payload = Snapshot is null ? null : SekibanJsonHelper.Deserialize<JsonElement?>(Snapshot);

        return new()
        {
            Id = new Guid(Id),
            AggregateId = new Guid(AggregateId),
            PartitionKey = PartitionKey,
            DocumentType = Enum.Parse<DocumentType>(DocumentType),
            DocumentTypeName = DocumentTypeName,
            TimeStamp = DateTimeConverter.ToDateTime(TimeStamp),
            SortableUniqueId = SortableUniqueId,
            AggregateType = AggregateType,
            RootPartitionKey = RootPartitionKey,
            Snapshot = payload,
            LastEventId = new Guid(LastEventId),
            LastSortableUniqueId = LastSortableUniqueId,
            SavedVersion = SavedVersion,
            PayloadVersionIdentifier = PayloadVersionIdentifier
        };
    }
}
