using Orleans;
using Sekiban.Pure.Aggregates;

namespace Sekiban.Pure.Dapr.Serialization;

/// <summary>
/// Surrogate for serializing aggregate state in Dapr
/// </summary>
[GenerateSerializer]
public class DaprAggregateSurrogate : DaprSurrogate<IAggregate>
{
    /// <summary>
    /// Compressed payload data
    /// </summary>
    [Id(0)]
    public byte[] CompressedPayload { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Type name or alias of the payload
    /// </summary>
    [Id(1)]
    public string PayloadTypeName { get; set; } = string.Empty;

    /// <summary>
    /// Aggregate version
    /// </summary>
    [Id(2)]
    public int Version { get; set; }

    /// <summary>
    /// Additional metadata
    /// </summary>
    [Id(3)]
    public Dictionary<string, string> Metadata { get; set; } = new();

    /// <summary>
    /// Aggregate ID
    /// </summary>
    [Id(4)]
    public Guid AggregateId { get; set; }

    /// <summary>
    /// Root partition key for multi-tenancy
    /// </summary>
    [Id(5)]
    public string RootPartitionKey { get; set; } = string.Empty;

    /// <summary>
    /// Last event ID processed
    /// </summary>
    [Id(6)]
    public string? LastEventId { get; set; }

    /// <summary>
    /// Indicates if payload is compressed
    /// </summary>
    [Id(7)]
    public bool IsCompressed { get; set; } = true;

    public override IAggregate ConvertFromSurrogate()
    {
        throw new NotImplementedException("Use DaprSerializationService for deserialization");
    }

    public override void ConvertToSurrogate(IAggregate value)
    {
        throw new NotImplementedException("Use DaprSerializationService for serialization");
    }
}