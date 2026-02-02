using Newtonsoft.Json;
using Sekiban.Dcb.Events;
using System.Linq;
namespace Sekiban.Dcb.CosmosDb.Models;

/// <summary>
///     Represents an event stored in CosmosDB events container
///     Partitioned by Id
/// </summary>
public class CosmosEvent
{
    /// <summary>
    ///     Composite partition key: "{serviceId}|{id}".
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
    public string Id { get; set; } = string.Empty;

    /// <summary>
    ///     Sortable unique ID for event ordering.
    /// </summary>
    [JsonProperty("sortableUniqueId")]
    public string SortableUniqueId { get; set; } = string.Empty;

    /// <summary>
    ///     Event payload type name.
    /// </summary>
    [JsonProperty("eventType")]
    public string EventType { get; set; } = string.Empty;

    /// <summary>
    ///     Serialized event payload.
    /// </summary>
    [JsonProperty("payload")]
    public string Payload { get; set; } = string.Empty;

    /// <summary>
    ///     Tags applied to the event.
    /// </summary>
    [JsonProperty("tags")]
    public IReadOnlyList<string> Tags { get; init; } = new List<string>();

    /// <summary>
    ///     Event timestamp (UTC).
    /// </summary>
    [JsonProperty("timestamp")]
    public DateTime Timestamp { get; set; }

    // Metadata fields
    /// <summary>
    ///     Causation identifier.
    /// </summary>
    [JsonProperty("causationId")]
    public string? CausationId { get; set; }

    /// <summary>
    ///     Correlation identifier.
    /// </summary>
    [JsonProperty("correlationId")]
    public string? CorrelationId { get; set; }

    /// <summary>
    ///     User that executed the event.
    /// </summary>
    [JsonProperty("executedUser")]
    public string? ExecutedUser { get; set; }

    /// <summary>
    ///     CosmosDB entity tag.
    /// </summary>
    [JsonProperty("_etag")]
    public string? ETag { get; set; }

    /// <summary>
    ///     Creates a CosmosDB event document from a domain event.
    /// </summary>
    public static CosmosEvent FromEvent(Event ev, string serializedPayload, string serviceId)
    {
        ArgumentNullException.ThrowIfNull(ev);
        ArgumentNullException.ThrowIfNull(serializedPayload);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceId);

        var id = ev.Id.ToString();

        return new CosmosEvent
        {
            Pk = $"{serviceId}|{id}",
            ServiceId = serviceId,
            Id = id,
            SortableUniqueId = ev.SortableUniqueIdValue,
            EventType = ev.EventType,
            Payload = serializedPayload,
            Tags = ev.Tags,
            Timestamp = DateTime.UtcNow,
            CausationId = ev.EventMetadata.CausationId,
            CorrelationId = ev.EventMetadata.CorrelationId,
            ExecutedUser = ev.EventMetadata.ExecutedUser
        };
    }

    /// <summary>
    ///     Converts a CosmosDB document back into a domain event.
    /// </summary>
    public Event ToEvent(IEventPayload deserializedPayload) =>
        new(
            deserializedPayload,
            SortableUniqueId,
            EventType,
            Guid.Parse(Id),
            new EventMetadata(CausationId ?? string.Empty, CorrelationId ?? string.Empty, ExecutedUser ?? string.Empty),
            Tags is List<string> list ? list : Tags.ToList());
}
