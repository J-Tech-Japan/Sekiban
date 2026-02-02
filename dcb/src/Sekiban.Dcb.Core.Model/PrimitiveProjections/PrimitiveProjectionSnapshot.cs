namespace Sekiban.Dcb.Primitives;

/// <summary>
///     Snapshot for primitive projection runtimes (e.g., WASM).
/// </summary>
public sealed record PrimitiveProjectionSnapshot(
    string ProjectorName,
    string ProjectorVersion,
    string StateJson,
    int Version,
    string? LastSortableUniqueId,
    DateTime PersistedAtUtc);
