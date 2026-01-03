namespace Sekiban.Dcb.MultiProjections;

/// <summary>
///     Persisted safe state for a multi projection.
/// </summary>
public sealed record MultiProjectionStateRecord(
    string ProjectorName,
    string ProjectorVersion,
    string PayloadType,
    string LastSortableUniqueId,
    long EventsProcessed,
    byte[]? StateData,
    bool IsOffloaded,
    string? OffloadKey,
    string? OffloadProvider,
    long OriginalSizeBytes,
    long CompressedSizeBytes,
    string SafeWindowThreshold,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string BuildSource,
    string? BuildHost)
{
    public string GetPartitionKey() => $"MultiProjectionState_{ProjectorName}";

    public string GetDocumentId() => ProjectorVersion;
}
