using Sekiban.Dcb.Events;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
namespace Sekiban.Dcb.Postgres.DbModels;

[Table("dcb_events")]
public class DbEvent
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; set; }

    [Required]
    public string SortableUniqueId { get; set; } = string.Empty;

    [Required]
    public string EventType { get; set; } = string.Empty;

    [Required]
    [Column(TypeName = "json")]
    public string Payload { get; set; } = string.Empty;

    [Column(TypeName = "jsonb")]
    public string Tags { get; set; } = "[]";

    public DateTime Timestamp { get; set; }

    // Metadata fields
    public string? CausationId { get; set; }
    public string? CorrelationId { get; set; }
    public string? ExecutedUser { get; set; }

    public static DbEvent FromEvent(Event ev, string serializedPayload) =>
        new()
        {
            Id = ev.Id,
            SortableUniqueId = ev.SortableUniqueIdValue,
            EventType = ev.EventType,
            Payload = serializedPayload,
            Tags = JsonSerializer.Serialize(ev.Tags),
            Timestamp = DateTime.UtcNow,
            CausationId = ev.EventMetadata.CausationId,
            CorrelationId = ev.EventMetadata.CorrelationId,
            ExecutedUser = ev.EventMetadata.ExecutedUser
        };

    public Event ToEvent(IEventPayload deserializedPayload)
    {
        var tagsList = string.IsNullOrEmpty(Tags) || Tags == "[]"
            ? new List<string>()
            : JsonSerializer.Deserialize<List<string>>(Tags) ?? new List<string>();

        return new Event(
            deserializedPayload,
            SortableUniqueId,
            EventType,
            Id,
            new EventMetadata(CausationId ?? string.Empty, CorrelationId ?? string.Empty, ExecutedUser ?? string.Empty),
            tagsList);
    }
}
