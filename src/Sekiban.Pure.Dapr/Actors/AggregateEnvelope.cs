using System.Text.Json.Serialization;

namespace Sekiban.Pure.Dapr.Actors;

/// <summary>
/// Envelope for passing aggregate state between components
/// Contains JSON-serialized aggregate payload that can be transported by Dapr
/// This is similar to CommandEnvelope but for aggregate state retrieval
/// </summary>
public record AggregateEnvelope
{
    /// <summary>
    /// The fully qualified type name of the aggregate
    /// </summary>
    [JsonPropertyName("aggregateType")]
    public string AggregateType { get; init; } = string.Empty;

    /// <summary>
    /// JSON-serialized aggregate payload converted to bytes
    /// </summary>
    [JsonPropertyName("aggregatePayload")]
    public byte[] AggregatePayload { get; init; } = Array.Empty<byte>();

    /// <summary>
    /// The aggregate ID
    /// </summary>
    [JsonPropertyName("aggregateId")]
    public Guid AggregateId { get; init; } = Guid.Empty;

    /// <summary>
    /// Partition ID for distributed scenarios
    /// </summary>
    [JsonPropertyName("aggregateGroup")]
    public string AggregateGroup { get; init; } = string.Empty;

    /// <summary>
    /// Root partition key for multi-tenancy
    /// </summary>
    [JsonPropertyName("rootPartitionKey")]
    public string RootPartitionKey { get; init; } = string.Empty;

    /// <summary>
    /// The current version of the aggregate
    /// </summary>
    [JsonPropertyName("version")]
    public int Version { get; init; }

    /// <summary>
    /// Last sortable unique ID for event ordering
    /// </summary>
    [JsonPropertyName("lastSortableUniqueId")]
    public string LastSortableUniqueId { get; init; } = string.Empty;

    /// <summary>
    /// Projector type name used to build this aggregate
    /// </summary>
    [JsonPropertyName("projectorTypeName")]
    public string ProjectorTypeName { get; init; } = string.Empty;

    /// <summary>
    /// Projector version for compatibility checking
    /// </summary>
    [JsonPropertyName("projectorVersion")]
    public string ProjectorVersion { get; init; } = string.Empty;

    /// <summary>
    /// Aggregate metadata
    /// </summary>
    [JsonPropertyName("metadata")]
    public Dictionary<string, string> Metadata { get; init; } = new();

    /// <summary>
    /// Creates a new AggregateEnvelope
    /// </summary>
    public AggregateEnvelope() { }

    /// <summary>
    /// Creates a new AggregateEnvelope with all properties
    /// </summary>
    public AggregateEnvelope(
        string aggregateType,
        byte[] aggregatePayload,
        Guid aggregateId,
        string aggregateGroup,
        string rootPartitionKey,
        int version,
        string lastSortableUniqueId,
        string projectorTypeName,
        string projectorVersion,
        Dictionary<string, string>? metadata = null)
    {
        AggregateType = aggregateType;
        AggregatePayload = aggregatePayload;
        AggregateId = aggregateId;
        AggregateGroup = aggregateGroup;
        RootPartitionKey = rootPartitionKey;
        Version = version;
        LastSortableUniqueId = lastSortableUniqueId;
        ProjectorTypeName = projectorTypeName;
        ProjectorVersion = projectorVersion;
        Metadata = metadata ?? new Dictionary<string, string>();
    }
}
