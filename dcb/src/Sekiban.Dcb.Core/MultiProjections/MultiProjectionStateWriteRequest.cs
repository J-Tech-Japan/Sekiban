namespace Sekiban.Dcb.MultiProjections;

/// <summary>
///     Request type for stream-based upsert with snapshot metadata only.
/// </summary>
public sealed record MultiProjectionStateWriteRequest(
    string ProjectorName,
    string ProjectorVersion,
    string PayloadType,
    string LastSortableUniqueId,
    long EventsProcessed,
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
