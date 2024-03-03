using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Documents;
using Sekiban.Core.Shared;
using Sekiban.Core.Snapshot;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace Sekiban.Infrastructure.Postgres.Databases;

public record DbCommandDocument
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; init; }
    public AggregateContainerGroup AggregateContainerGroup { get; init; } = AggregateContainerGroup.Default;
    [Column(TypeName = "jsonb")]
    public string Payload { get; init; } = string.Empty;
    public string? ExecutedUser { get; init; }
    public string? Exception { get; init; }
    [Column(TypeName = "jsonb")]
    public string CallHistories { get; init; } = string.Empty;
    public Guid AggregateId { get; init; }
    public string PartitionKey { get; init; } = string.Empty;
    public DocumentType DocumentType { get; init; } = DocumentType.Event;
    public string DocumentTypeName { get; init; } = string.Empty;
    public DateTime TimeStamp { get; init; } = DateTime.MinValue;
    public string SortableUniqueId { get; init; } = string.Empty;
    public string AggregateType { get; init; } = string.Empty;
    public string RootPartitionKey { get; init; } = string.Empty;

    public static DbCommandDocument FromCommandDocument(ICommandDocumentCommon commandDocument, AggregateContainerGroup aggregateContainerGroup) =>
        new()
        {
            AggregateContainerGroup = aggregateContainerGroup,
            Payload = SekibanJsonHelper.Serialize(commandDocument.GetPayload()) ?? string.Empty,
            ExecutedUser = commandDocument.ExecutedUser,
            Exception = commandDocument.Exception,
            CallHistories = SekibanJsonHelper.Serialize(commandDocument.CallHistories) ?? string.Empty,
            Id = commandDocument.Id,
            AggregateId = commandDocument.AggregateId,
            PartitionKey = commandDocument.PartitionKey,
            DocumentType = commandDocument.DocumentType,
            DocumentTypeName = commandDocument.DocumentTypeName,
            TimeStamp = commandDocument.TimeStamp,
            SortableUniqueId = commandDocument.SortableUniqueId,
            AggregateType = commandDocument.AggregateType,
            RootPartitionKey = commandDocument.RootPartitionKey
        };
}
public record DbMultiProjectionDocument
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; init; }
    public AggregateContainerGroup AggregateContainerGroup { get; init; } = AggregateContainerGroup.Default;
    public string PartitionKey { get; init; } = string.Empty;
    public DocumentType DocumentType { get; init; } = DocumentType.Event;
    public string DocumentTypeName { get; init; } = string.Empty;
    public DateTime TimeStamp { get; init; } = DateTime.MinValue;
    public string SortableUniqueId { get; init; } = string.Empty;
    public string AggregateType { get; init; } = string.Empty;
    public string RootPartitionKey { get; init; } = string.Empty;
    public Guid LastEventId { get; init; }

    public string LastSortableUniqueId { get; init; } = string.Empty;

    public int SavedVersion { get; init; }

    public string PayloadVersionIdentifier { get; init; } = string.Empty;
    public static DbMultiProjectionDocument
        FromMultiProjectionSnapshotDocument(MultiProjectionSnapshotDocument commandDocument, AggregateContainerGroup aggregateContainerGroup) =>
        new()
        {
            AggregateContainerGroup = aggregateContainerGroup,
            Id = commandDocument.Id,
            PartitionKey = commandDocument.PartitionKey,
            DocumentType = commandDocument.DocumentType,
            DocumentTypeName = commandDocument.DocumentTypeName,
            TimeStamp = commandDocument.TimeStamp,
            SortableUniqueId = commandDocument.SortableUniqueId,
            AggregateType = commandDocument.AggregateType,
            RootPartitionKey = commandDocument.RootPartitionKey,
            LastEventId = commandDocument.LastEventId,
            LastSortableUniqueId = commandDocument.LastSortableUniqueId,
            SavedVersion = commandDocument.SavedVersion,
            PayloadVersionIdentifier = commandDocument.PayloadVersionIdentifier
        };
    public MultiProjectionSnapshotDocument ToMultiProjectionSnapshotDocument() =>
        new()
        {
            Id = Id,
            PartitionKey = PartitionKey,
            DocumentType = DocumentType,
            DocumentTypeName = DocumentTypeName,
            TimeStamp = TimeStamp,
            SortableUniqueId = SortableUniqueId,
            AggregateType = AggregateType,
            RootPartitionKey = RootPartitionKey,
            LastEventId = LastEventId,
            LastSortableUniqueId = LastSortableUniqueId,
            SavedVersion = SavedVersion,
            PayloadVersionIdentifier = PayloadVersionIdentifier
        };
}
