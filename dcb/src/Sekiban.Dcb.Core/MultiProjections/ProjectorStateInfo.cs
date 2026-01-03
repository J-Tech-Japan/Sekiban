namespace Sekiban.Dcb.MultiProjections;

/// <summary>
///     Summary of stored projection state.
/// </summary>
public sealed record ProjectorStateInfo(
    string ProjectorName,
    string ProjectorVersion,
    long EventsProcessed,
    DateTime UpdatedAt,
    long OriginalSizeBytes,
    long CompressedSizeBytes,
    string LastSortableUniqueId);
