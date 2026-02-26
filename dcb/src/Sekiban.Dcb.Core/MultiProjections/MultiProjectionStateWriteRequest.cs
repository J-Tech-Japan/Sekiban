namespace Sekiban.Dcb.MultiProjections;

/// <summary>
///     Request type for stream-based upsert. StateData is nullable to support
///     offloaded snapshots where data lives in blob storage.
/// </summary>
public sealed record MultiProjectionStateWriteRequest(
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
    /// <summary>
    ///     Converts this write request to a <see cref="MultiProjectionStateRecord" />.
    /// </summary>
    public MultiProjectionStateRecord ToRecord() =>
        new(
            ProjectorName: ProjectorName,
            ProjectorVersion: ProjectorVersion,
            PayloadType: PayloadType,
            LastSortableUniqueId: LastSortableUniqueId,
            EventsProcessed: EventsProcessed,
            StateData: StateData,
            IsOffloaded: IsOffloaded,
            OffloadKey: OffloadKey,
            OffloadProvider: OffloadProvider,
            OriginalSizeBytes: OriginalSizeBytes,
            CompressedSizeBytes: CompressedSizeBytes,
            SafeWindowThreshold: SafeWindowThreshold,
            CreatedAt: CreatedAt,
            UpdatedAt: UpdatedAt,
            BuildSource: BuildSource,
            BuildHost: BuildHost);
}
