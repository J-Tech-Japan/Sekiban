using Sekiban.Core.Documents;
using Sekiban.Core.Events;
using Sekiban.Core.Shared;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace Sekiban.Infrastructure.Postgres.Databases;

public record DbEvent : IDbEvent
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; init; }
    [Column(TypeName = "json")]
    public string Payload { get; init; } = string.Empty;
    public int Version { get; init; }
    [Column(TypeName = "json")]
    public string CallHistories { get; init; } = string.Empty;
    public Guid AggregateId { get; init; }
    public string PartitionKey { get; init; } = string.Empty;
    public DocumentType DocumentType { get; init; } = DocumentType.Event;
    public string DocumentTypeName { get; init; } = string.Empty;
    public DateTime TimeStamp { get; init; } = DateTime.MinValue;
    public string SortableUniqueId { get; init; } = string.Empty;
    public string AggregateType { get; init; } = string.Empty;
    public string RootPartitionKey { get; init; } = string.Empty;

    public static DbEvent FromEvent(IEvent ev) =>
        new()
        {
            Version = ev.Version,
            Payload = SekibanJsonHelper.Serialize(ev.GetPayload()) ?? string.Empty,
            CallHistories = SekibanJsonHelper.Serialize(ev.CallHistories) ?? string.Empty,
            Id = ev.Id,
            AggregateId = ev.AggregateId,
            PartitionKey = ev.PartitionKey,
            DocumentType = ev.DocumentType,
            DocumentTypeName = ev.DocumentTypeName,
            TimeStamp = ev.TimeStamp,
            SortableUniqueId = ev.SortableUniqueId,
            AggregateType = ev.AggregateType,
            RootPartitionKey = ev.RootPartitionKey
        };
}
