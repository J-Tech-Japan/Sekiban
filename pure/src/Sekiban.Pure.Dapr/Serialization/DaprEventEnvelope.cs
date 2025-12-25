using Orleans;
namespace Sekiban.Pure.Dapr.Serialization;

/// <summary>
///     Envelope for serializing events in Dapr state store
/// </summary>
[GenerateSerializer]
public class DaprEventEnvelope
{
    /// <summary>
    ///     Unique event ID
    /// </summary>
    [Id(0)]
    public Guid EventId { get; set; }

    /// <summary>
    ///     Serialized event data
    /// </summary>
    [Id(1)]
    public byte[] EventData { get; set; } = Array.Empty<byte>();

    /// <summary>
    ///     Event type name or alias
    /// </summary>
    [Id(2)]
    public string EventType { get; set; } = string.Empty;

    /// <summary>
    ///     Aggregate ID this event belongs to
    /// </summary>
    [Id(3)]
    public Guid AggregateId { get; set; }

    /// <summary>
    ///     Version number of this event in the aggregate
    /// </summary>
    [Id(4)]
    public int Version { get; set; }

    /// <summary>
    ///     Timestamp when the event occurred
    /// </summary>
    [Id(5)]
    public DateTime Timestamp { get; set; }

    /// <summary>
    ///     Additional metadata for the event
    /// </summary>
    [Id(6)]
    public Dictionary<string, string> Metadata { get; set; } = new();

    /// <summary>
    ///     Root partition key for multi-tenancy
    /// </summary>
    [Id(7)]
    public string RootPartitionKey { get; set; } = string.Empty;

    /// <summary>
    ///     Indicates if event data is compressed
    /// </summary>
    [Id(8)]
    public bool IsCompressed { get; set; } = true;

    /// <summary>
    ///     Sortable unique ID for event ordering
    /// </summary>
    [Id(9)]
    public string SortableUniqueId { get; set; } = string.Empty;
}
