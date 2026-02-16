namespace Sekiban.Dcb.Runtime;

/// <summary>
///     Metadata about the multi-projection state, part of the primitive contract.
///     Placed in Core layer so both Native and WASM implementations can produce it.
/// </summary>
public sealed record MultiProjectionStateMetadata(
    string ProjectorName,
    string ProjectorVersion,
    bool IsCatchedUp,
    int SafeVersion,
    string? SafeLastSortableUniqueId,
    int UnsafeVersion,
    string? UnsafeLastSortableUniqueId,
    Guid? UnsafeLastEventId,
    bool IsSafeState);
