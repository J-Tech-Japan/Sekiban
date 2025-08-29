namespace Sekiban.Dcb.Snapshots;

/// <summary>
///     Snapshot payload prepared for persistence: serialized JSON and associated metadata.
/// </summary>
public sealed record SnapshotPersistenceData(
    string Json,
    int Size,
    string SafeLastSortableUniqueId);

