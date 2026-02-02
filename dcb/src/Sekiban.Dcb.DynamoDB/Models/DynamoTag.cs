using Amazon.DynamoDBv2.Model;

namespace Sekiban.Dcb.DynamoDB.Models;

/// <summary>
///     DynamoDB model for tag storage.
/// </summary>
public class DynamoTag
{
    /// <summary>
    ///     Partition key: SERVICE#{serviceId}#TAG#{tagString}
    /// </summary>
    public string Pk { get; set; } = string.Empty;

    /// <summary>
    ///     Sort key: {sortableUniqueId}#{eventId} for uniqueness and ordering.
    /// </summary>
    public string Sk { get; set; } = string.Empty;

    /// <summary>
    ///     Service ID for tenant isolation.
    /// </summary>
    public string ServiceId { get; set; } = string.Empty;

    /// <summary>
    ///     Full tag string.
    /// </summary>
    public string TagString { get; set; } = string.Empty;

    /// <summary>
    ///     Tag group (category).
    /// </summary>
    public string TagGroup { get; set; } = string.Empty;

    /// <summary>
    ///     Event type.
    /// </summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>
    ///     Reference to event ID.
    /// </summary>
    public string EventId { get; set; } = string.Empty;

    /// <summary>
    ///     Sortable unique ID for ordering.
    /// </summary>
    public string SortableUniqueId { get; set; } = string.Empty;

    /// <summary>
    ///     Creation timestamp (ISO8601).
    /// </summary>
    public string CreatedAt { get; set; } = string.Empty;

    /// <summary>
    ///     Creates a DynamoTag from event tag data.
    /// </summary>
    public static DynamoTag FromEventTag(
        string serviceId,
        string tagString,
        string tagGroup,
        string sortableUniqueId,
        Guid eventId,
        string eventType)
    {
        return new DynamoTag
        {
            Pk = $"SERVICE#{serviceId}#TAG#{tagString}",
            Sk = $"{sortableUniqueId}#{eventId}",
            ServiceId = serviceId,
            TagString = tagString,
            TagGroup = tagGroup,
            EventType = eventType,
            EventId = eventId.ToString(),
            SortableUniqueId = sortableUniqueId,
            CreatedAt = DateTimeOffset.UtcNow.ToString("O")
        };
    }

    /// <summary>
    ///     Converts to DynamoDB attribute values.
    /// </summary>
    public Dictionary<string, AttributeValue> ToAttributeValues()
    {
        return new Dictionary<string, AttributeValue>
        {
            ["pk"] = new AttributeValue { S = Pk },
            ["sk"] = new AttributeValue { S = Sk },
            ["serviceId"] = new AttributeValue { S = ServiceId },
            ["tagString"] = new AttributeValue { S = TagString },
            ["tagGroup"] = new AttributeValue { S = TagGroup },
            ["eventType"] = new AttributeValue { S = EventType },
            ["eventId"] = new AttributeValue { S = EventId },
            ["sortableUniqueId"] = new AttributeValue { S = SortableUniqueId },
            ["createdAt"] = new AttributeValue { S = CreatedAt }
        };
    }

    /// <summary>
    ///     Creates from DynamoDB attribute values.
    /// </summary>
    public static DynamoTag FromAttributeValues(Dictionary<string, AttributeValue> item)
    {
        return new DynamoTag
        {
            Pk = item.GetValueOrDefault("pk")?.S ?? string.Empty,
            Sk = item.GetValueOrDefault("sk")?.S ?? string.Empty,
            ServiceId = item.GetValueOrDefault("serviceId")?.S ?? string.Empty,
            TagString = item.GetValueOrDefault("tagString")?.S ?? string.Empty,
            TagGroup = item.GetValueOrDefault("tagGroup")?.S ?? string.Empty,
            EventType = item.GetValueOrDefault("eventType")?.S ?? string.Empty,
            EventId = item.GetValueOrDefault("eventId")?.S ?? string.Empty,
            SortableUniqueId = item.GetValueOrDefault("sortableUniqueId")?.S ?? string.Empty,
            CreatedAt = item.GetValueOrDefault("createdAt")?.S ?? string.Empty
        };
    }
}
