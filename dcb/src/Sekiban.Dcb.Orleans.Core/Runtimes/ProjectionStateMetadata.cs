namespace Sekiban.Dcb.Runtime;

/// <summary>
///     Primitive-only metadata about the projection state.
///     No domain types, no framework dependencies.
/// </summary>
public sealed record ProjectionStateMetadata(
    string ProjectorName,
    string ProjectorVersion,
    bool IsCatchedUp,
    int UnsafeVersion,
    string? UnsafeLastSortableUniqueId,
    Guid? UnsafeLastEventId,
    int SafeVersion,
    string? SafeLastSortableUniqueId);
