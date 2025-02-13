using ResultBoxes;
using Sekiban.Pure.Events;
using Sekiban.Pure.Serialize;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
namespace Sekiban.Pure.Postgres;

public record DbEvent : IDbEvent
{


    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; init; }

    [Column(TypeName = "json")]
    public string Payload { get; init; } = string.Empty;
    public string SortableUniqueId { get; init; } = string.Empty;

    public int Version { get; init; }
    public Guid AggregateId { get; init; }
    public string RootPartitionKey { get; init; } = string.Empty;
    public DateTime TimeStamp { get; init; } = DateTime.MinValue;
    public string PartitionKey { get; init; } = string.Empty;

    public string AggregateGroup { get; init; } = string.Empty;
    public string PayloadTypeName { get; init; } = string.Empty;
    public string CausationId { get; init; } = string.Empty;
    public string CorrelationId { get; init; } = string.Empty;
    public string ExecutedUser { get; init; } = string.Empty;

    public static DbEvent FromEvent(IEvent ev, ISekibanSerializer serializer, IEventTypes eventTypes)
    {
        var document = eventTypes.ConvertToEventDocument(ev).UnwrapBox();
        var payloadJson = eventTypes.SerializePayloadToJson(serializer, ev).UnwrapBox();
        return new DbEvent
        {
            Id = ev.Id,
            Payload = payloadJson,
            SortableUniqueId = document.SortableUniqueId,
            Version = document.Version,
            AggregateId = document.AggregateId,
            RootPartitionKey = document.RootPartitionKey,
            TimeStamp = document.TimeStamp,
            PartitionKey = document.PartitionKey,
            AggregateGroup = document.AggregateGroup,
            PayloadTypeName = document.PayloadTypeName,
            CausationId = document.Metadata.CausationId,
            CorrelationId = document.Metadata.CorrelationId,
            ExecutedUser = document.Metadata.ExecutedUser
        };
    }
}