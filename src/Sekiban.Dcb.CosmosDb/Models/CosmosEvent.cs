using Newtonsoft.Json;
using Sekiban.Dcb.Events;
namespace Sekiban.Dcb.CosmosDb.Models;

/// <summary>
///     Represents an event stored in CosmosDB events container
///     Partitioned by Id
/// </summary>
public class CosmosEvent
{
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("sortableUniqueId")]
    public string SortableUniqueId { get; set; } = string.Empty;

    [JsonProperty("eventType")]
    public string EventType { get; set; } = string.Empty;

    [JsonProperty("payload")]
    public string Payload { get; set; } = string.Empty;

    [JsonProperty("tags")]
    public List<string> Tags { get; set; } = new();

    [JsonProperty("timestamp")]
    public DateTime Timestamp { get; set; }

    // Metadata fields
    [JsonProperty("causationId")]
    public string? CausationId { get; set; }

    [JsonProperty("correlationId")]
    public string? CorrelationId { get; set; }

    [JsonProperty("executedUser")]
    public string? ExecutedUser { get; set; }

    [JsonProperty("_etag")]
    public string? ETag { get; set; }

    public static CosmosEvent FromEvent(Event ev, string serializedPayload) =>
        new()
        {
            Id = ev.Id.ToString(),
            SortableUniqueId = ev.SortableUniqueIdValue,
            EventType = ev.EventType,
            Payload = serializedPayload,
            Tags = ev.Tags,
            Timestamp = DateTime.UtcNow,
            CausationId = ev.EventMetadata.CausationId,
            CorrelationId = ev.EventMetadata.CorrelationId,
            ExecutedUser = ev.EventMetadata.ExecutedUser
        };

    public Event ToEvent(IEventPayload deserializedPayload) =>
        new(
            deserializedPayload,
            SortableUniqueId,
            EventType,
            Guid.Parse(Id),
            new EventMetadata(CausationId ?? string.Empty, CorrelationId ?? string.Empty, ExecutedUser ?? string.Empty),
            Tags);
}