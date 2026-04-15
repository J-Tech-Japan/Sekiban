using System.Text;
using System.Text.Json.Serialization;

namespace Sekiban.Dcb.MultiProjections;

/// <summary>
///     マルチプロジェクション状態をシリアライズ可能な形で保持するコア領域専用のレコードです。
/// </summary>
public record SerializableMultiProjectionState
{
    /// <summary>
    ///     Legacy: JSON payload as UTF-8 string (v9 format).
    ///     Kept for backward compatibility.
    /// </summary>
    public string? PayloadJson { get; init; }

    /// <summary>
    ///     Base64 encoded payload bytes (v10 format).
    ///     Supports arbitrary binary formats (MessagePack, Gzip, etc.)
    /// </summary>
    public string? PayloadBase64 { get; init; }

    public string MultiProjectionPayloadType { get; init; }
    public string ProjectorName { get; init; }
    public string ProjectorVersion { get; init; }
    public string LastSortableUniqueId { get; init; }
    public Guid LastEventId { get; init; }
    public int Version { get; init; }
    public bool IsCatchedUp { get; init; } = true;
    public bool IsSafeState { get; init; } = true;

    /// <summary>
    ///     Original (uncompressed) payload size in bytes.
    /// </summary>
    public long OriginalSizeBytes { get; init; }

    /// <summary>
    ///     Compressed payload size in bytes.
    /// </summary>
    public long CompressedSizeBytes { get; init; }

    /// <summary>
    ///     Runtime-only raw payload bytes used to avoid Base64 encode/decode churn
    ///     when an offloaded snapshot is materialized back into an inline state.
    /// </summary>
    [JsonIgnore]
    public byte[]? RuntimePayloadBytes { get; init; }

    /// <summary>
    ///     JSON deserialization constructor - parameter names must match property names.
    /// </summary>
    [JsonConstructor]
    public SerializableMultiProjectionState(
        string? payloadJson,
        string? payloadBase64,
        string multiProjectionPayloadType,
        string projectorName,
        string projectorVersion,
        string lastSortableUniqueId,
        Guid lastEventId,
        int version,
        bool isCatchedUp = true,
        bool isSafeState = true,
        long originalSizeBytes = 0,
        long compressedSizeBytes = 0)
    {
        PayloadJson = payloadJson;
        PayloadBase64 = payloadBase64;
        MultiProjectionPayloadType = multiProjectionPayloadType;
        ProjectorName = projectorName;
        ProjectorVersion = projectorVersion;
        LastSortableUniqueId = lastSortableUniqueId;
        LastEventId = lastEventId;
        Version = version;
        IsCatchedUp = isCatchedUp;
        IsSafeState = isSafeState;
        OriginalSizeBytes = originalSizeBytes;
        CompressedSizeBytes = compressedSizeBytes;
    }

    /// <summary>
    ///     Creates state from byte[] payload using Base64 encoding (v10 format).
    ///     Supports arbitrary binary data including compressed or MessagePack serialized data.
    /// </summary>
    public static SerializableMultiProjectionState FromBytes(
        byte[] payload,
        string multiProjectionPayloadType,
        string projectorName,
        string projectorVersion,
        string lastSortableUniqueId,
        Guid lastEventId,
        int version,
        bool isCatchedUp = true,
        bool isSafeState = true,
        long originalSizeBytes = 0,
        long compressedSizeBytes = 0)
    {
        // If sizes not provided, use payload length for both
        if (originalSizeBytes == 0) originalSizeBytes = payload.LongLength;
        if (compressedSizeBytes == 0) compressedSizeBytes = payload.LongLength;

        return new SerializableMultiProjectionState(
            payloadJson: null,  // v10: Use PayloadBase64 instead
            payloadBase64: Convert.ToBase64String(payload),
            multiProjectionPayloadType,
            projectorName,
            projectorVersion,
            lastSortableUniqueId,
            lastEventId,
            version,
            isCatchedUp,
            isSafeState,
            originalSizeBytes,
            compressedSizeBytes);
    }

    /// <summary>
    ///     Creates a runtime-only inline state without Base64-encoding the payload.
    ///     This is intended for transient restore flows and should not be serialized.
    /// </summary>
    public static SerializableMultiProjectionState FromRuntimeBytes(
        byte[] payload,
        string multiProjectionPayloadType,
        string projectorName,
        string projectorVersion,
        string lastSortableUniqueId,
        Guid lastEventId,
        int version,
        bool isCatchedUp = true,
        bool isSafeState = true,
        long originalSizeBytes = 0,
        long compressedSizeBytes = 0)
    {
        if (originalSizeBytes == 0) originalSizeBytes = payload.LongLength;
        if (compressedSizeBytes == 0) compressedSizeBytes = payload.LongLength;

        return new SerializableMultiProjectionState(
            payloadJson: null,
            payloadBase64: null,
            multiProjectionPayloadType,
            projectorName,
            projectorVersion,
            lastSortableUniqueId,
            lastEventId,
            version,
            isCatchedUp,
            isSafeState,
            originalSizeBytes,
            compressedSizeBytes)
        {
            RuntimePayloadBytes = payload
        };
    }

    /// <summary>
    ///     Gets the payload as bytes for deserialization.
    ///     Supports both v10 (Base64) and v9 (UTF-8 JSON) formats.
    /// </summary>
    public byte[] GetPayloadBytes()
    {
        if (RuntimePayloadBytes is not null)
        {
            return RuntimePayloadBytes;
        }

        // v10 format: Base64 encoded binary
        if (!string.IsNullOrEmpty(PayloadBase64))
        {
            return Convert.FromBase64String(PayloadBase64);
        }
        // v9 format: UTF-8 JSON string (backward compatibility)
        if (!string.IsNullOrEmpty(PayloadJson))
        {
            return Encoding.UTF8.GetBytes(PayloadJson);
        }
        return Array.Empty<byte>();
    }
}
