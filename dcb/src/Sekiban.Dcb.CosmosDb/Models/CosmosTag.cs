using Newtonsoft.Json;
namespace Sekiban.Dcb.CosmosDb.Models;

/// <summary>
///     Represents a tag reference to an event in the CosmosDB tags container
///     Partitioned by Tag
/// </summary>
public class CosmosTag
{
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty; // Unique ID for the document

    [JsonProperty("tag")]
    public string Tag { get; set; } = string.Empty; // Partition key

    [JsonProperty("tagGroup")]
    public string TagGroup { get; set; } = string.Empty;

    [JsonProperty("eventType")]
    public string EventType { get; set; } = string.Empty;

    [JsonProperty("sortableUniqueId")]
    public string SortableUniqueId { get; set; } = string.Empty;

    [JsonProperty("eventId")]
    public string EventId { get; set; } = string.Empty;

    [JsonProperty("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonProperty("_etag")]
    public string? ETag { get; set; }

    public static CosmosTag FromEventTag(string tag, string tagGroup, string sortableUniqueId, Guid eventId, string eventType) =>
        new()
        {
            Id = Guid.NewGuid().ToString(), // Generate unique ID for the document
            Tag = tag,
            TagGroup = tagGroup,
            EventType = eventType,
            SortableUniqueId = sortableUniqueId,
            EventId = eventId.ToString(),
            CreatedAt = DateTime.UtcNow
        };
}