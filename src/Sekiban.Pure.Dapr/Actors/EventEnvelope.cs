using System.Text.Json.Serialization;

namespace Sekiban.Pure.Dapr.Actors;

/// <summary>
/// Envelope for passing events between AggregateActor and AggregateEventHandlerActor
/// Contains Protobuf-serialized event payload that can be JSON-serialized by Dapr
/// </summary>
public record EventEnvelope
{
    /// <summary>
    /// The fully qualified type name of the event
    /// </summary>
    [JsonPropertyName("eventType")]
    public string EventType { get; init; } = string.Empty;

    /// <summary>
    /// Protobuf-serialized event payload
    /// </summary>
    [JsonPropertyName("eventPayload")]
    public byte[] EventPayload { get; init; } = Array.Empty<byte>();

    /// <summary>
    /// The aggregate ID this event belongs to
    /// </summary>
    [JsonPropertyName("aggregateId")]
    public string AggregateId { get; init; } = string.Empty;

    /// <summary>
    /// Partition ID for distributed scenarios
    /// </summary>
    [JsonPropertyName("partitionId")]
    public Guid PartitionId { get; init; } = Guid.Empty;

    /// <summary>
    /// Root partition key for multi-tenancy
    /// </summary>
    [JsonPropertyName("rootPartitionKey")]
    public string RootPartitionKey { get; init; } = string.Empty;

    /// <summary>
    /// Event version/sequence number
    /// </summary>
    [JsonPropertyName("version")]
    public int Version { get; init; }

    /// <summary>
    /// Sortable unique ID for the event
    /// </summary>
    [JsonPropertyName("sortableUniqueId")]
    public string SortableUniqueId { get; init; } = string.Empty;

    /// <summary>
    /// Timestamp when the event was created
    /// </summary>
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Event metadata
    /// </summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, string> Metadata { get; init; } = new();

    /// <summary>
    /// Correlation ID for tracking
    /// </summary>
    [JsonPropertyName("correlationId")]
    public string CorrelationId { get; init; } = string.Empty;

    /// <summary>
    /// The ID of the command that caused this event
    /// </summary>
    [JsonPropertyName("causationId")]
    public string CausationId { get; init; } = string.Empty;

    /// <summary>
    /// Creates a new EventEnvelope
    /// </summary>
    public EventEnvelope() { }

    /// <summary>
    /// Creates a new EventEnvelope with all properties
    /// </summary>
    public EventEnvelope(
        string eventType,
        byte[] eventPayload,
        string aggregateId,
        Guid partitionId,
        string rootPartitionKey,
        int version,
        string sortableUniqueId,
        Dictionary<string, string>? metadata = null,
        string? correlationId = null,
        string? causationId = null)
    {
        EventType = eventType;
        EventPayload = eventPayload;
        AggregateId = aggregateId;
        PartitionId = partitionId;
        RootPartitionKey = rootPartitionKey;
        Version = version;
        SortableUniqueId = sortableUniqueId;
        Timestamp = DateTime.UtcNow;
        Metadata = metadata ?? new Dictionary<string, string>();
        CorrelationId = correlationId ?? Guid.NewGuid().ToString();
        CausationId = causationId ?? string.Empty;
    }
}

/// <summary>
/// Response from event handling
/// </summary>
public record EventHandlingResponse
{
    /// <summary>
    /// Whether the event was handled successfully
    /// </summary>
    [JsonPropertyName("isSuccess")]
    public bool IsSuccess { get; init; }

    /// <summary>
    /// Error details if handling failed
    /// </summary>
    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// The last processed event ID
    /// </summary>
    [JsonPropertyName("lastProcessedEventId")]
    public string? LastProcessedEventId { get; init; }

    /// <summary>
    /// Creates a new EventHandlingResponse
    /// </summary>
    public EventHandlingResponse() { }

    /// <summary>
    /// Creates a successful EventHandlingResponse
    /// </summary>
    public static EventHandlingResponse Success(string lastProcessedEventId)
    {
        return new EventHandlingResponse
        {
            IsSuccess = true,
            LastProcessedEventId = lastProcessedEventId
        };
    }

    /// <summary>
    /// Creates a failed EventHandlingResponse
    /// </summary>
    public static EventHandlingResponse Failure(string errorMessage)
    {
        return new EventHandlingResponse
        {
            IsSuccess = false,
            ErrorMessage = errorMessage
        };
    }
}