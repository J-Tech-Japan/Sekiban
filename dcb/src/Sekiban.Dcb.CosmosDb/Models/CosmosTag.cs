using Newtonsoft.Json;
namespace Sekiban.Dcb.CosmosDb.Models;

/// <summary>
///     Represents a tag reference to an event in the CosmosDB tags container
///     Partitioned by Tag
/// </summary>
public class CosmosTag
{
    /// <summary>
    ///     Composite partition key: "{serviceId}|{tag}".
    /// </summary>
    [JsonProperty("pk")]
    public string Pk { get; set; } = string.Empty;

    /// <summary>
    ///     Service ID for tenant isolation.
    /// </summary>
    [JsonProperty("serviceId")]
    public string ServiceId { get; set; } = string.Empty;

    /// <summary>
    ///     CosmosDB document ID.
    /// </summary>
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty; // Unique ID for the document

    /// <summary>
    ///     Tag string.
    /// </summary>
    [JsonProperty("tag")]
    public string Tag { get; set; } = string.Empty; // Partition key

    /// <summary>
    ///     Tag group name.
    /// </summary>
    [JsonProperty("tagGroup")]
    public string TagGroup { get; set; } = string.Empty;

    /// <summary>
    ///     Event type name.
    /// </summary>
    [JsonProperty("eventType")]
    public string EventType { get; set; } = string.Empty;

    /// <summary>
    ///     Sortable unique ID for ordering.
    /// </summary>
    [JsonProperty("sortableUniqueId")]
    public string SortableUniqueId { get; set; } = string.Empty;

    /// <summary>
    ///     Associated event ID.
    /// </summary>
    [JsonProperty("eventId")]
    public string EventId { get; set; } = string.Empty;

    /// <summary>
    ///     Document creation timestamp (UTC).
    /// </summary>
    [JsonProperty("createdAt")]
    public DateTime CreatedAt { get; set; }

    /// <summary>
    ///     CosmosDB entity tag.
    /// </summary>
    [JsonProperty("_etag")]
    public string? ETag { get; set; }

    /// <summary>
    ///     Creates a tag document from event metadata.
    /// </summary>
    public static CosmosTag FromEventTag(
        string tag,
        string tagGroup,
        string sortableUniqueId,
        Guid eventId,
        string eventType,
        string serviceId) =>
        new()
        {
            Pk = $"{serviceId}|{tag}",
            ServiceId = serviceId,
            Id = Guid.NewGuid().ToString(), // Generate unique ID for the document
            Tag = tag,
            TagGroup = tagGroup,
            EventType = eventType,
            SortableUniqueId = sortableUniqueId,
            EventId = eventId.ToString(),
            CreatedAt = DateTime.UtcNow
        };
}
