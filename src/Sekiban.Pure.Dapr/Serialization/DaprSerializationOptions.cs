using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace Sekiban.Pure.Dapr.Serialization;

/// <summary>
///     Options for configuring Dapr serialization
/// </summary>
public class DaprSerializationOptions
{
    /// <summary>
    ///     Whether to enable compression for payloads
    /// </summary>
    public bool EnableCompression { get; set; } = true;

    /// <summary>
    ///     Compression level to use when compression is enabled
    /// </summary>
    public CompressionLevel CompressionLevel { get; set; } = CompressionLevel.Optimal;

    /// <summary>
    ///     Minimum size in bytes before compression is applied
    /// </summary>
    public int CompressionThreshold { get; set; } = 1024; // 1KB

    /// <summary>
    ///     Whether to use type aliases instead of full type names
    /// </summary>
    public bool EnableTypeAliases { get; set; } = true;

    /// <summary>
    ///     JSON serializer options
    /// </summary>
    public JsonSerializerOptions JsonSerializerOptions { get; set; } = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    /// <summary>
    ///     Default options instance
    /// </summary>
    public static DaprSerializationOptions Default => new();
}
