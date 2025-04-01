using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Text.Json;
using ResultBoxes;
using Sekiban.Pure.Projectors;

namespace Sekiban.Pure.Orleans.Parts;

/// <summary>
/// Serializable state for MultiProjector grains that avoids interface serialization issues.
/// This class compresses and stores the multiProjector state as JSON strings rather than
/// attempting to directly serialize interface types, which System.Text.Json cannot handle.
/// </summary>
[GenerateSerializer]
public record SerializableMultiProjectionState
{
    /// <summary>
    /// The compressed JSON representation of the IMultiProjectorCommon Payload
    /// </summary>
    [Id(0)]
    public byte[]? CompressedPayloadJson { get; init; }
    
    /// <summary>
    /// The full type name of the Payload
    /// </summary>
    [Id(1)]
    public string PayloadTypeName { get; init; } = string.Empty;
    
    /// <summary>
    /// Version identifier for the Payload type
    /// </summary>
    [Id(2)]
    public string PayloadVersion { get; init; } = string.Empty;
    
    /// <summary>
    /// Last event ID processed by the projector
    /// </summary>
    [Id(3)]
    public Guid LastEventId { get; init; }
    
    /// <summary>
    /// Last sortable unique ID processed by the projector
    /// </summary>
    [Id(4)]
    public string LastSortableUniqueId { get; init; } = string.Empty;
    
    /// <summary>
    /// Version of the state
    /// </summary>
    [Id(5)]
    public int Version { get; init; }
    
    /// <summary>
    /// The version of the snapshot that was applied
    /// </summary>
    [Id(6)]
    public int AppliedSnapshotVersion { get; init; }
    
    /// <summary>
    /// The root partition key
    /// </summary>
    [Id(7)]
    public string RootPartitionKey { get; init; } = string.Empty;

    /// <summary>
    /// Creates a SerializableMultiProjectionState from a MultiProjectionState
    /// </summary>
    /// <param name="state">The source MultiProjectionState to convert</param>
    /// <param name="options">JSON serializer options</param>
    /// <typeparam name="TProjection">The projector type</typeparam>
    /// <returns>A serializable representation of the state</returns>
    public static async Task<SerializableMultiProjectionState> CreateFromAsync<TProjection>(
        MultiProjectionState state, 
        JsonSerializerOptions options) 
        where TProjection : IMultiProjectorCommon
    {
        if (state.ProjectorCommon is not TProjection projector)
        {
            throw new InvalidOperationException(
                $"Projector is not of expected type. Expected {typeof(TProjection).FullName}, but got {state.ProjectorCommon?.GetType().FullName ?? "null"}");
        }
        
        var payloadJson = JsonSerializer.Serialize(projector, options);
        var compressedPayload = await CompressStringAsync(payloadJson);
        
        var assembly = typeof(TProjection).Assembly;
        var versionString = assembly.GetName().Version?.ToString() ?? "0.0.0.0";
        
        return new SerializableMultiProjectionState
        {
            CompressedPayloadJson = compressedPayload,
            PayloadTypeName = typeof(TProjection).AssemblyQualifiedName ?? string.Empty,
            PayloadVersion = versionString,
            LastEventId = state.LastEventId,
            LastSortableUniqueId = state.LastSortableUniqueId,
            Version = state.Version,
            AppliedSnapshotVersion = state.AppliedSnapshotVersion,
            RootPartitionKey = state.RootPartitionKey
        };
    }

    /// <summary>
    /// Converts this SerializableMultiProjectionState back to a MultiProjectionState
    /// </summary>
    /// <param name="options">JSON serializer options</param>
    /// <typeparam name="TProjection">The projector type</typeparam>
    /// <returns>An optional containing the MultiProjectionState if conversion was successful, or None if not</returns>
    public async Task<OptionalValue<MultiProjectionState>> ToMultiProjectionStateAsync<TProjection>(
        JsonSerializerOptions options) 
        where TProjection : IMultiProjectorCommon
    {
        // Verify type compatibility
        var expectedTypeName = typeof(TProjection).AssemblyQualifiedName;
        if (!string.Equals(PayloadTypeName, expectedTypeName, StringComparison.Ordinal))
        {
            // Type mismatch - return None to indicate failure
            return OptionalValue<MultiProjectionState>.None;
        }
        
        // Verify version compatibility
        var currentAssembly = typeof(TProjection).Assembly;
        var currentVersion = currentAssembly.GetName().Version?.ToString() ?? "0.0.0.0";
        
        if (!string.Equals(PayloadVersion, currentVersion, StringComparison.Ordinal))
        {
            // Version mismatch - return None to indicate failure
            // This allows the caller to rebuild the state from scratch if needed
            return OptionalValue<MultiProjectionState>.None;
        }
        
        if (CompressedPayloadJson == null)
        {
            return OptionalValue<MultiProjectionState>.None;
        }
        
        try
        {
            // Decompress and deserialize the payload
            var payloadJson = await DecompressStringAsync(CompressedPayloadJson);
            var projector = JsonSerializer.Deserialize<TProjection>(payloadJson, options);
            
            if (projector == null)
            {
                return OptionalValue<MultiProjectionState>.None;
            }
            
            // Recreate the MultiProjectionState
            var state = new MultiProjectionState(
                projector,
                LastEventId,
                LastSortableUniqueId,
                Version,
                AppliedSnapshotVersion,
                RootPartitionKey);
            
            return new OptionalValue<MultiProjectionState>(state);
        }
        catch (Exception)
        {
            // Any exception during deserialization means we can't recover the state
            return OptionalValue<MultiProjectionState>.None;
        }
    }
    
    /// <summary>
    /// Compresses a string using GZip compression
    /// </summary>
    private static async Task<byte[]> CompressStringAsync(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        
        using var outputStream = new MemoryStream();
        using (var gzipStream = new GZipStream(outputStream, CompressionLevel.Optimal, true))
        {
            await gzipStream.WriteAsync(bytes);
        }
        
        return outputStream.ToArray();
    }
    
    /// <summary>
    /// Decompresses a GZip compressed byte array back to a string
    /// </summary>
    private static async Task<string> DecompressStringAsync(byte[] compressedData)
    {
        using var inputStream = new MemoryStream(compressedData);
        using var outputStream = new MemoryStream();
        using (var gzipStream = new GZipStream(inputStream, CompressionMode.Decompress))
        {
            await gzipStream.CopyToAsync(outputStream);
        }
        
        return Encoding.UTF8.GetString(outputStream.ToArray());
    }
}
