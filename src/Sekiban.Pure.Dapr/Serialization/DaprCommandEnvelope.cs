using Orleans;

namespace Sekiban.Pure.Dapr.Serialization;

/// <summary>
/// Envelope for serializing commands in Dapr actors
/// </summary>
[GenerateSerializer]
public class DaprCommandEnvelope
{
    /// <summary>
    /// Serialized command data
    /// </summary>
    [Id(0)]
    public byte[] CommandData { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Command type name or alias
    /// </summary>
    [Id(1)]
    public string CommandType { get; set; } = string.Empty;

    /// <summary>
    /// Additional headers for the command
    /// </summary>
    [Id(2)]
    public Dictionary<string, string> Headers { get; set; } = new();

    /// <summary>
    /// Correlation ID for tracking
    /// </summary>
    [Id(3)]
    public string CorrelationId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Timestamp when the command was created
    /// </summary>
    [Id(4)]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Indicates if command data is compressed
    /// </summary>
    [Id(5)]
    public bool IsCompressed { get; set; } = true;
}