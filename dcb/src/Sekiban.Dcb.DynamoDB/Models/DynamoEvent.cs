using Amazon.DynamoDBv2.Model;
using Sekiban.Dcb.Events;

namespace Sekiban.Dcb.DynamoDB.Models;

/// <summary>
///     DynamoDB model for event storage.
/// </summary>
public class DynamoEvent
{
    /// <summary>
    ///     Partition key: EVENT#{eventId}
    /// </summary>
    public string Pk { get; set; } = string.Empty;

    /// <summary>
    ///     Sort key: EVENT#{eventId}
    /// </summary>
    public string Sk { get; set; } = string.Empty;

    /// <summary>
    ///     GSI1 partition key for chronological queries.
    /// </summary>
    public string Gsi1Pk { get; set; } = string.Empty;

    /// <summary>
    ///     Event ID (UUID).
    /// </summary>
    public string EventId { get; set; } = string.Empty;

    /// <summary>
    ///     Sortable unique ID for ordering.
    /// </summary>
    public string SortableUniqueId { get; set; } = string.Empty;

    /// <summary>
    ///     Event type name.
    /// </summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>
    ///     JSON serialized payload.
    /// </summary>
    public string Payload { get; set; } = string.Empty;

    /// <summary>
    ///     Tag strings associated with this event.
    /// </summary>
    public IReadOnlyList<string> Tags { get; private set; } = Array.Empty<string>();

    /// <summary>
    ///     Event timestamp (ISO8601).
    /// </summary>
    public string Timestamp { get; set; } = string.Empty;

    /// <summary>
    ///     Causation ID (command ID).
    /// </summary>
    public string? CausationId { get; set; }

    /// <summary>
    ///     Correlation ID.
    /// </summary>
    public string? CorrelationId { get; set; }

    /// <summary>
    ///     Executing user.
    /// </summary>
    public string? ExecutedUser { get; set; }

    /// <summary>
    ///     Creates a DynamoEvent from a domain Event.
    /// </summary>
    public static DynamoEvent FromEvent(Event ev, string serializedPayload, string gsi1Pk)
    {
        ArgumentNullException.ThrowIfNull(ev);
        ArgumentNullException.ThrowIfNull(serializedPayload);

        return new DynamoEvent
        {
            Pk = $"EVENT#{ev.Id}",
            Sk = $"EVENT#{ev.Id}",
            Gsi1Pk = gsi1Pk,
            EventId = ev.Id.ToString(),
            SortableUniqueId = ev.SortableUniqueIdValue,
            EventType = ev.EventType,
            Payload = serializedPayload,
            Tags = ev.Tags.ToList(),
            Timestamp = DateTimeOffset.UtcNow.ToString("O"),
            CausationId = ev.EventMetadata.CausationId,
            CorrelationId = ev.EventMetadata.CorrelationId,
            ExecutedUser = ev.EventMetadata.ExecutedUser
        };
    }

    /// <summary>
    ///     Converts to a domain Event with the provided payload.
    /// </summary>
    public Event ToEvent(IEventPayload payload)
    {
        return new Event(
            payload,
            SortableUniqueId,
            EventType,
            Guid.Parse(EventId),
            new EventMetadata(
                CausationId ?? string.Empty,
                CorrelationId ?? string.Empty,
                ExecutedUser ?? string.Empty),
            Tags.ToList());
    }

    /// <summary>
    ///     Converts to DynamoDB attribute values.
    /// </summary>
    public Dictionary<string, AttributeValue> ToAttributeValues()
    {
        var item = new Dictionary<string, AttributeValue>
        {
            ["pk"] = new AttributeValue { S = Pk },
            ["sk"] = new AttributeValue { S = Sk },
            ["gsi1pk"] = new AttributeValue { S = Gsi1Pk },
            ["eventId"] = new AttributeValue { S = EventId },
            ["sortableUniqueId"] = new AttributeValue { S = SortableUniqueId },
            ["eventType"] = new AttributeValue { S = EventType },
            ["payload"] = new AttributeValue { S = Payload },
            ["tags"] = new AttributeValue { L = Tags.Select(t => new AttributeValue { S = t }).ToList() },
            ["timestamp"] = new AttributeValue { S = Timestamp }
        };

        if (!string.IsNullOrEmpty(CausationId))
            item["causationId"] = new AttributeValue { S = CausationId };
        if (!string.IsNullOrEmpty(CorrelationId))
            item["correlationId"] = new AttributeValue { S = CorrelationId };
        if (!string.IsNullOrEmpty(ExecutedUser))
            item["executedUser"] = new AttributeValue { S = ExecutedUser };

        return item;
    }

    /// <summary>
    ///     Creates from DynamoDB attribute values.
    /// </summary>
    public static DynamoEvent FromAttributeValues(Dictionary<string, AttributeValue> item)
    {
        return new DynamoEvent
        {
            Pk = item.GetValueOrDefault("pk")?.S ?? string.Empty,
            Sk = item.GetValueOrDefault("sk")?.S ?? string.Empty,
            Gsi1Pk = item.GetValueOrDefault("gsi1pk")?.S ?? string.Empty,
            EventId = item.GetValueOrDefault("eventId")?.S ?? string.Empty,
            SortableUniqueId = item.GetValueOrDefault("sortableUniqueId")?.S ?? string.Empty,
            EventType = item.GetValueOrDefault("eventType")?.S ?? string.Empty,
            Payload = item.GetValueOrDefault("payload")?.S ?? string.Empty,
            Tags = item.GetValueOrDefault("tags")?.L?.Select(a => a.S).ToList() ?? [],
            Timestamp = item.GetValueOrDefault("timestamp")?.S ?? string.Empty,
            CausationId = item.GetValueOrDefault("causationId")?.S,
            CorrelationId = item.GetValueOrDefault("correlationId")?.S,
            ExecutedUser = item.GetValueOrDefault("executedUser")?.S
        };
    }
}
