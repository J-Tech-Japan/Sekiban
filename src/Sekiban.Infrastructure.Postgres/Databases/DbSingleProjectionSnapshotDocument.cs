using Sekiban.Core.Aggregate;
using Sekiban.Core.Documents;
using Sekiban.Core.Shared;
using Sekiban.Core.Snapshot;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace Sekiban.Infrastructure.Postgres.Databases;

public record DbSingleProjectionSnapshotDocument
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; init; }
    public AggregateContainerGroup AggregateContainerGroup { get; init; } = AggregateContainerGroup.Default;
    [Column(TypeName = "jsonb")]
    public string? Snapshot { get; init; }
    public Guid LastEventId { get; init; }
    public string LastSortableUniqueId { get; init; } = string.Empty;
    public int SavedVersion { get; init; }

    public string PayloadVersionIdentifier { get; init; } = string.Empty;
    public string CallHistories { get; init; } = string.Empty;
    public Guid AggregateId { get; init; }
    public string PartitionKey { get; init; } = string.Empty;
    public DocumentType DocumentType { get; init; } = DocumentType.Event;
    public string DocumentTypeName { get; init; } = string.Empty;
    public DateTime TimeStamp { get; init; } = DateTime.MinValue;
    public string SortableUniqueId { get; init; } = string.Empty;
    public string AggregateType { get; init; } = string.Empty;
    public string RootPartitionKey { get; init; } = string.Empty;

    public static DbSingleProjectionSnapshotDocument FromDocument(SnapshotDocument document, AggregateContainerGroup aggregateContainerGroup) =>
        new()
        {
            AggregateContainerGroup = aggregateContainerGroup,
            Snapshot = document.Snapshot is null ? null : SekibanJsonHelper.Serialize(document.Snapshot),
            LastEventId = document.LastEventId,
            LastSortableUniqueId = document.LastSortableUniqueId,
            SavedVersion = document.SavedVersion,
            PayloadVersionIdentifier = document.PayloadVersionIdentifier,
            Id = document.Id,
            AggregateId = document.AggregateId,
            PartitionKey = document.PartitionKey,
            DocumentType = document.DocumentType,
            DocumentTypeName = document.DocumentTypeName,
            TimeStamp = document.TimeStamp,
            SortableUniqueId = document.SortableUniqueId,
            AggregateType = document.AggregateType,
            RootPartitionKey = document.RootPartitionKey
        };
}
